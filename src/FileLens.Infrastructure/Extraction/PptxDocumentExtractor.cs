using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed partial class PptxDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pptx", ".pptm", ".potx", ".potm"
    };

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    [GeneratedRegex(@"slide(\d+)\.xml", RegexOptions.IgnoreCase)]
    private static partial Regex SlideNumberRegex();

    public PptxDocumentExtractor(ITextNormalizer normalizer, ILogger<PptxDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "PptxDocumentExtractor";
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

        // Find all slide entries
        var slideEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e =>
            {
                var match = SlideNumberRegex().Match(e.FullName);
                int num = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
                return (Number: num, Entry: e);
            })
            .OrderBy(s => s.Number)
            .ToList();

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;

        foreach (var (slideNum, entry) in slideEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var slideStream = entry.Open();
            var xDoc = await XDocument.LoadAsync(slideStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);

            var slideTextBuilder = new StringBuilder();
            foreach (var tElem in xDoc.Descendants(A + "t"))
            {
                var val = tElem.Value;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    slideTextBuilder.AppendLine(val.Trim());
                }
            }

            var slideContent = slideTextBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(slideContent))
            {
                fullTextBuilder.AppendLine($"[Slide {slideNum}]");
                fullTextBuilder.AppendLine(slideContent);

                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "Slide",
                    $"Slide {slideNum}",
                    slideContent,
                    slideNumber: slideNum));
            }
        }

        metadata["SlideCount"] = slideEntries.Count.ToString();

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

            if (!string.IsNullOrWhiteSpace(title)) metadata["Title"] = title;
            if (!string.IsNullOrWhiteSpace(creator)) metadata["Author"] = creator;
        }
        catch
        {
        }
    }
}

