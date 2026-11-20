using System.Text;
using System.Text.Json;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class JsonDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".jsonl", ".ndjson"
    };

    public JsonDocumentExtractor(ITextNormalizer normalizer, ILogger<JsonDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "JsonDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(file.FullPath).ToLowerInvariant();
        if (ext is ".jsonl" or ".ndjson")
        {
            return await ExtractJsonLinesAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (fileInfo.Length == 0)
        {
            return ExtractedDocument.CreateSuccess(
                file.Id.Value,
                file.FullPath,
                ExtractorId,
                string.Empty,
                string.Empty,
                Array.Empty<DocumentChunk>());
        }

        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 64
        };

        using var document = await JsonDocument.ParseAsync(stream, options, cancellationToken).ConfigureAwait(false);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            int elementIndex = 0;
            var batchBuilder = new StringBuilder();
            int batchStart = 0;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = FlattenElement(element);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    batchBuilder.AppendLine(text);
                    fullTextBuilder.AppendLine(text);
                }

                elementIndex++;
                if (elementIndex % 25 == 0)
                {
                    var chunkText = batchBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunkText))
                    {
                        chunks.Add(CreateChunk(
                            file.Id.Value,
                            chunkIndex++,
                            "JsonArray",
                            $"Elements [{batchStart}-{elementIndex - 1}]",
                            chunkText));
                    }
                    batchBuilder.Clear();
                    batchStart = elementIndex;
                }
            }

            if (batchBuilder.Length > 0)
            {
                var chunkText = batchBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "JsonArray",
                        $"Elements [{batchStart}-{elementIndex - 1}]",
                        chunkText));
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var propText = FlattenElement(property.Value);
                var fullPropText = $"{property.Name}: {propText}";
                fullTextBuilder.AppendLine(fullPropText);

                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "JsonProperty",
                    $"Key '{property.Name}'",
                    fullPropText));
            }
        }
        else
        {
            var text = FlattenElement(document.RootElement);
            fullTextBuilder.Append(text);
            chunks.Add(CreateChunk(file.Id.Value, chunkIndex++, "JsonValue", "Root", text));
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["RootKind"] = document.RootElement.ValueKind.ToString()
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

    private async Task<ExtractedDocument> ExtractJsonLinesAsync(IndexedFile file, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        var batchBuilder = new StringBuilder();
        int lineCount = 0;
        int chunkIndex = 0;
        int batchStart = 1;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lineCount++;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var text = FlattenElement(doc.RootElement);
                batchBuilder.AppendLine(text);
                fullTextBuilder.AppendLine(text);
            }
            catch (JsonException)
            {
                batchBuilder.AppendLine(line);
                fullTextBuilder.AppendLine(line);
            }

            if (lineCount % 25 == 0)
            {
                var chunkText = batchBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "JsonLines",
                        $"Lines {batchStart}-{lineCount}",
                        chunkText));
                }
                batchBuilder.Clear();
                batchStart = lineCount + 1;
            }
        }

        if (batchBuilder.Length > 0)
        {
            var chunkText = batchBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(chunkText))
            {
                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "JsonLines",
                    $"Lines {batchStart}-{lineCount}",
                    chunkText));
            }
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["LineCount"] = lineCount.ToString()
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

    private static string FlattenElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetRawText();
            case JsonValueKind.Object:
            {
                var sb = new StringBuilder();
                foreach (var prop in element.EnumerateObject())
                {
                    var val = FlattenElement(prop.Value);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(prop.Name).Append(": ").Append(val);
                    }
                }
                return sb.ToString();
            }
            case JsonValueKind.Array:
            {
                var sb = new StringBuilder();
                foreach (var item in element.EnumerateArray())
                {
                    var val = FlattenElement(item);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(val);
                    }
                }
                return sb.ToString();
            }
            default:
                return string.Empty;
        }
    }
}

