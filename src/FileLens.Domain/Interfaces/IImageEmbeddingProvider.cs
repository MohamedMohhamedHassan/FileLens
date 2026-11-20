using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Creates embeddings for full images and selected crops for visual search.
/// </summary>
public interface IImageEmbeddingProvider
{
    string ProviderName { get; }
    string ModelName { get; }
    string ModelVersion { get; }
    string ModelRevision { get; }
    string Preprocessing { get; }
    int Dimension { get; }
    Task<VisualImageDescriptor> CreateEmbeddingAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

