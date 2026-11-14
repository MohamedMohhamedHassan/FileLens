using System.Text;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class PlainTextExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".env", ".text"
    };

    public PlainTextExtractor(ITextNormalizer normalizer, ILogger<PlainTextExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "PlainTextExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        var paragraphBuilder = new StringBuilder();
        int chunkIndex = 0;
        int paragraphStartLine = 1;
        int currentLineNumber = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            currentLineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                if (paragraphBuilder.Length > 0)
                {
                    var paraText = paragraphBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        fullTextBuilder.AppendLine(paraText);
                        chunks.Add(CreateChunk(
                            file.Id.Value,
                            chunkIndex++,
                            "Paragraph",
                            $"Lines {paragraphStartLine}-{currentLineNumber - 1}",
                            paraText));
                    }
                    paragraphBuilder.Clear();
                }
                paragraphStartLine = currentLineNumber + 1;
            }
            else
            {
                if (paragraphBuilder.Length > 0)
                {
                    paragraphBuilder.AppendLine();
                }
                paragraphBuilder.Append(line);

                // If paragraph is very large (e.g. > 100 lines without blank line), split chunk
                if (currentLineNumber - paragraphStartLine >= 100)
                {
                    var paraText = paragraphBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        fullTextBuilder.AppendLine(paraText);
                        chunks.Add(CreateChunk(
                            file.Id.Value,
                            chunkIndex++,
                            "Lines",
                            $"Lines {paragraphStartLine}-{currentLineNumber}",
                            paraText));
                    }
                    paragraphBuilder.Clear();
                    paragraphStartLine = currentLineNumber + 1;
                }
            }
        }

        if (paragraphBuilder.Length > 0)
        {
            var paraText = paragraphBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(paraText))
            {
                fullTextBuilder.AppendLine(paraText);
                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "Paragraph",
                    $"Lines {paragraphStartLine}-{currentLineNumber}",
                    paraText));
            }
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["LineCount"] = currentLineNumber.ToString(),
            ["Encoding"] = reader.CurrentEncoding.WebName
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

