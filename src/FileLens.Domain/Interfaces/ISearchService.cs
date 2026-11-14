using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

public interface ISearchService
{
    Task<SearchResultsPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
    Task<ExtractedDocument?> GetDocumentPreviewAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<ExtractionStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

