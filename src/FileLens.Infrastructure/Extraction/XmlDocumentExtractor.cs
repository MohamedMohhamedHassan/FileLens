using System.Text;
using System.Xml;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class XmlDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xaml", ".svg", ".config", ".manifest"
    };

    public XmlDocumentExtractor(ITextNormalizer normalizer, ILogger<XmlDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "XmlDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = XmlReader.Create(stream, settings);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        var currentChunkBuilder = new StringBuilder();
        int chunkIndex = 0;
        string currentElement = "Root";

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    currentElement = reader.LocalName;
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                        {
                            if (!string.IsNullOrWhiteSpace(reader.Value))
                            {
                                currentChunkBuilder.Append(reader.Name).Append("=\"").Append(reader.Value).Append("\" ");
                                fullTextBuilder.Append(reader.Value).Append(' ');
                            }
                        }
                        reader.MoveToElement();
                    }
                    break;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    var text = reader.Value;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        currentChunkBuilder.AppendLine(text);
                        fullTextBuilder.AppendLine(text);

                        if (currentChunkBuilder.Length > 2000)
                        {
                            var chunkContent = currentChunkBuilder.ToString().Trim();
                            chunks.Add(CreateChunk(
                                file.Id.Value,
                                chunkIndex++,
                                "XmlElement",
                                $"Element <{currentElement}>",
                                chunkContent));
                            currentChunkBuilder.Clear();
                        }
                    }
                    break;
            }
        }

        if (currentChunkBuilder.Length > 0)
        {
            var chunkContent = currentChunkBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "XmlElement",
                    $"Element <{currentElement}>",
                    chunkContent));
            }
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["RootElement"] = currentElement
        };

        return ExtractedDocument.CreateSuccess(
            file.Id.Value,
            file.FullPath,
            ExtractorId,
            fullRaw,
            fullNorm,
            chunks,
            metadata);
    }
}

