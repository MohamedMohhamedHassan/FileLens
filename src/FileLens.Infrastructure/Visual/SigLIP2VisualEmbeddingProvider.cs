using System.Text.Json;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Infrastructure.Visual;

public sealed class SigLIP2VisualEmbeddingProvider : IImageEmbeddingProvider
{
    private readonly VisualWorkerClient _worker;
    public SigLIP2VisualEmbeddingProvider(VisualWorkerClient worker) => _worker = worker;
    public string ProviderName => "SigLIP2VisualEmbeddingProvider";
    public string ModelName => "google/siglip2-base-patch16-224";
    public string ModelVersion => "0ad8c6e0ff16615356a08a1ad8c8bbc8930c434e";
    public string ModelRevision => ModelVersion;
    public string Preprocessing => "AutoProcessor local RGB 224x224; model-native normalization; L2 normalized image features";
    public int Dimension => 768;

    public async Task<VisualImageDescriptor> CreateEmbeddingAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var response = await _worker.EmbedImageAsync(imageBytes, cancellationToken) ?? throw new InvalidOperationException("SigLIP worker returned no response.");
        var result = response.GetProperty("result");
        var vector = result.GetProperty("vector").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (vector.Length != Dimension) throw new InvalidOperationException($"SigLIP vector dimension {vector.Length} does not match {Dimension}.");
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (Math.Abs(norm - 1) > 0.001) throw new InvalidOperationException("SigLIP vector is not normalized.");
        var embedding = new ImageEmbedding(ModelName, ModelVersion, vector, ProviderName, ModelRevision, Preprocessing);
        return new VisualImageDescriptor(embedding, result.GetProperty("width").GetInt32(), result.GetProperty("height").GetInt32(), string.Empty);
    }
}
