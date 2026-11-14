using System.Text;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class DelimitedTextExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv"
    };

    public DelimitedTextExtractor(ITextNormalizer normalizer, ILogger<DelimitedTextExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "DelimitedTextExtractor";
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
        var batchBuilder = new StringBuilder();
        int chunkIndex = 0;
        int rowCount = 0;
        int batchStartRow = 1;
        string? headerLine = null;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            rowCount++;
            if (rowCount == 1)
            {
                headerLine = line;
                batchBuilder.AppendLine(line);
                fullTextBuilder.AppendLine(line);
                continue;
            }

            batchBuilder.AppendLine(line);
            fullTextBuilder.AppendLine(line);

            if (rowCount % 50 == 0)
            {
                var text = batchBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "TableRows",
                        $"Rows {batchStartRow}-{rowCount}",
                        text));
                }
                batchBuilder.Clear();
                if (headerLine != null)
                {
                    batchBuilder.AppendLine(headerLine); // Include header context
                }
                batchStartRow = rowCount + 1;
            }
        }

        if (batchBuilder.Length > 0 && batchStartRow <= rowCount)
        {
            var text = batchBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "TableRows",
                    $"Rows {batchStartRow}-{rowCount}",
                    text));
            }
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["RowCount"] = rowCount.ToString(),
            ["HasHeader"] = (headerLine != null).ToString()
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

