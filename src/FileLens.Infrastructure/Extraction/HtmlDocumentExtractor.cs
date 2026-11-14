using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public sealed partial class HtmlDocumentExtractor : BaseDocumentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml"
    };

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex MultipleNewlinesRegex();

    public HtmlDocumentExtractor(ITextNormalizer normalizer, ILogger<HtmlDocumentExtractor> logger)
        : base(normalizer, logger)
    {
    }

    public override string ExtractorId => "HtmlDocumentExtractor";
    public override IReadOnlySet<string> SupportedExtensions => Extensions;

    protected override async Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        string title = string.Empty;
        var titleMatch = TitleRegex().Match(html);
        if (titleMatch.Success)
        {
            title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
        }

        // Strip scripts, styles, comments
        var clean = ScriptTagRegex().Replace(html, " ");
        clean = StyleTagRegex().Replace(clean, " ");
        clean = CommentRegex().Replace(clean, " ");

        // Replace common block tags with newlines to preserve paragraph boundaries
        clean = Regex.Replace(clean, @"</?(p|div|h[1-6]|li|tr|blockquote|br)[^>]*>", "\n", RegexOptions.IgnoreCase);

        // Strip all remaining tags
        clean = HtmlTagRegex().Replace(clean, " ");

        // Decode HTML entities
        clean = WebUtility.HtmlDecode(clean);

        // Normalize newlines
        clean = MultipleNewlinesRegex().Replace(clean, "\n\n").Trim();

        var paragraphs = clean.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<DocumentChunk>();
        var fullTextBuilder = new StringBuilder();
        int chunkIndex = 0;

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var p = paragraphs[i].Trim();
            if (string.IsNullOrWhiteSpace(p)) continue;

            fullTextBuilder.AppendLine(p);
            chunks.Add(CreateChunk(
                file.Id.Value,
                chunkIndex++,
                "Section",
                $"Section {chunkIndex}",
                p));
        }

        var fullRaw = fullTextBuilder.ToString().Trim();
        var fullNorm = Normalizer.NormalizeText(fullRaw);

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            metadata["Title"] = title;
        }

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

