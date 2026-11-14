using System.Diagnostics;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Application.Services;

public sealed class SearchService : ISearchService
{
    private readonly IIndexDatabase _database;
    private readonly ITextNormalizer _normalizer;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        IIndexDatabase database,
        ITextNormalizer normalizer,
        ILogger<SearchService> logger)
    {
        _database = database;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<SearchResultsPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return new SearchResultsPage
            {
                Items = Array.Empty<SearchResult>(),
                TotalMatches = 0,
                SearchDurationMs = 0,
                QueryUsed = string.Empty
            };
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Executing full-text search: '{Query}'", query.Text);

        var page = await _database.SearchFullTextAsync(query, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        _logger.LogInformation("Search for '{Query}' returned {Count} results in {Ms} ms",
            query.Text, page.TotalMatches, sw.ElapsedMilliseconds);

        return new SearchResultsPage
        {
            Items = page.Items,
            TotalMatches = page.TotalMatches,
            SearchDurationMs = sw.ElapsedMilliseconds,
            QueryUsed = query.Text
        };
    }

    public async Task<ExtractedDocument?> GetDocumentPreviewAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        return await _database.GetExtractedDocumentByFileIdAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExtractionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return await _database.GetExtractionStatisticsAsync(cancellationToken).ConfigureAwait(false);
    }
}
