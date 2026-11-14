using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class DocxDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".dotx", ".dotm"
    };

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    public DocxDocumentExtractor(ITextNormalizer normalizer, ILogger<DocxDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "DocxDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var metadata = new Dictionary<string, string>();
        ReadCoreMetadata(archive, metadata);

        var docEntry = archive.GetEntry("word/document.xml");
        if (docEntry == null)
        {
            throw new InvalidDataException("Invalid DOCX format: word/document.xml entry missing");
        }

        using var docStream = docEntry.Open();
        var xDoc = await XDocument.LoadAsync(docStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);

        var body = xDoc.Root?.Element(W + "body");
        if (body == null)
        {
            return ExtractedDocument.CreateSuccess(file.Id.Value, file.FullPath, ExtractorId, string.Empty, string.Empty, Array.Empty<DocumentChunk>(), metadata);
        }

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;
        int paragraphIndex = 0;
        int tableIndex = 0;

        foreach (var element in body.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element.Name == W + "p")
            {
                paragraphIndex++;
                var text = ExtractParagraphText(element);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    fullTextBuilder.AppendLine(text);
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "Paragraph",
                        $"Paragraph {paragraphIndex}",
                        text));
                }
            }
            else if (element.Name == W + "tbl")
            {
                tableIndex++;
                var tableText = ExtractTableText(element);
                if (!string.IsNullOrWhiteSpace(tableText))
                {
                    fullTextBuilder.AppendLine(tableText);
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "Table",
                        $"Table {tableIndex}",
                        tableText));
                }
            }
        }

        metadata["ParagraphCount"] = paragraphIndex.ToString();
        metadata["TableCount"] = tableIndex.ToString();

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        return ExtractedDocument.CreateSuccess(
            file.Id.Value,
            file.FullPath,
            ExtractorId,
            fullRaw,
            fullNorm,
            chunks,
            metadata);
    }

    private static string ExtractParagraphText(XElement paragraphElement)
    {
        var sb = new StringBuilder();
        foreach (var run in paragraphElement.Elements(W + "r"))
        {
            foreach (var child in run.Elements())
            {
                if (child.Name == W + "t")
                {
                    sb.Append(child.Value);
                }
                else if (child.Name == W + "tab")
                {
                    sb.Append('\t');
                }
                else if (child.Name == W + "br")
                {
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString().Trim();
    }

    private static string ExtractTableText(XElement tableElement)
    {
        var sb = new StringBuilder();
        foreach (var row in tableElement.Elements(W + "tr"))
        {
            var cellTexts = new List<string>();
            foreach (var cell in row.Elements(W + "tc"))
            {
                var cellText = string.Join(" ", cell.Elements(W + "p").Select(ExtractParagraphText)).Trim();
                cellTexts.Add(cellText);
            }
            if (cellTexts.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                sb.AppendLine(string.Join(" | ", cellTexts));
            }
        }
        return sb.ToString().Trim();
    }

    private static void ReadCoreMetadata(ZipArchive archive, Dictionary<string, string> metadata)
    {
        var coreEntry = archive.GetEntry("docProps/core.xml");
        if (coreEntry == null) return;

        try
        {
            using var s = coreEntry.Open();
            var doc = XDocument.Load(s);
            var title = doc.Root?.Element(Dc + "title")?.Value;
            var creator = doc.Root?.Element(Dc + "creator")?.Value;
            var subject = doc.Root?.Element(Dc + "subject")?.Value;
            var modifiedBy = doc.Root?.Element(Cp + "lastModifiedBy")?.Value;

            if (!string.IsNullOrWhiteSpace(title)) metadata["Title"] = title;
            if (!string.IsNullOrWhiteSpace(creator)) metadata["Author"] = creator;
            if (!string.IsNullOrWhiteSpace(subject)) metadata["Subject"] = subject;
            if (!string.IsNullOrWhiteSpace(modifiedBy)) metadata["LastModifiedBy"] = modifiedBy;
        }
        catch
        {
            // Ignore non-critical metadata read failure
        }
    }
}

