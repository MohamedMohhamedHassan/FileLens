using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Adds, removes, updates and queries vectors for visual similarity search.
/// </summary>
public interface IVectorIndex
{
    Task UpsertAsync(VisualEmbedding embedding, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VisualSearchResult>> SearchAsync(float[] queryVector, int limit, double threshold, string? providerName = null, string? modelName = null, string? modelVersion = null, CancellationToken cancellationToken = default);
}

