using System.Text;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed class SourceCodeExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".js", ".ts", ".jsx", ".tsx",
        ".cpp", ".c", ".h", ".hpp", ".cc", ".cxx",
        ".java", ".go", ".rs", ".sql", ".sh", ".bash",
        ".ps1", ".bat", ".cmd", ".md", ".yaml", ".yml",
        ".proto", ".graphql", ".php", ".rb", ".swift", ".kt"
    };

    public SourceCodeExtractor(ITextNormalizer normalizer, ILogger<SourceCodeExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "SourceCodeExtractor";
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
        var blockBuilder = new StringBuilder();
        int chunkIndex = 0;
        int currentLine = 0;
        int blockStartLine = 1;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            currentLine++;
            blockBuilder.AppendLine(line);
            fullTextBuilder.AppendLine(line);

            if (currentLine % 50 == 0)
            {
                var blockText = blockBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(blockText))
                {
                    chunks.Add(CreateChunk(
                        file.Id.Value,
                        chunkIndex++,
                        "SourceCode",
                        $"Lines {blockStartLine}-{currentLine}",
                        blockText));
                }
                blockBuilder.Clear();
                blockStartLine = currentLine + 1;
            }
        }

        if (blockBuilder.Length > 0 && blockStartLine <= currentLine)
        {
            var blockText = blockBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(blockText))
            {
                chunks.Add(CreateChunk(
                    file.Id.Value,
                    chunkIndex++,
                    "SourceCode",
                    $"Lines {blockStartLine}-{currentLine}",
                    blockText));
            }
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>
        {
            ["TotalLines"] = currentLine.ToString(),
            ["Language"] = Path.GetExtension(file.FullPath).TrimStart('.').ToUpperInvariant()
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

