using System.Text.Json;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Infrastructure.Visual;

/// <summary>Calls the local worker only; no image bytes leave the machine.</summary>
public sealed class LocalVisualEmbeddingProvider : IImageEmbeddingProvider
{
    private readonly Ocr.OcrWorkerClient _worker;
    public string ProviderName => "LocalVisualEmbeddingProvider";
    public string ModelName => "FileLens-VisualFeature-CPU";
    public string ModelVersion => "1.0";
    public string ModelRevision => "1.0";
    public string Preprocessing => "OpenCV 64x64 RGB/grayscale/edge histograms and spatial statistics";
    public int Dimension => 104;

    public LocalVisualEmbeddingProvider(Ocr.OcrWorkerClient worker) => _worker = worker;

    public async Task<VisualImageDescriptor> CreateEmbeddingAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            command = "image_embedding",
            id = Guid.NewGuid().ToString("N"),
            image_base64 = Convert.ToBase64String(imageBytes)
        };
        var response = await _worker.SendCommandAsync(request, TimeSpan.FromSeconds(60), cancellationToken)
            ?? throw new InvalidOperationException("Visual embedding worker did not return a response.");
        if (response.TryGetProperty("status", out var status) && status.GetString() != "success")
            throw new InvalidOperationException(response.TryGetProperty("error", out var error) ? error.GetString() : "Visual embedding failed.");
        var result = response.GetProperty("result");
        var vector = result.GetProperty("vector").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return new VisualImageDescriptor(
            new ImageEmbedding(result.GetProperty("model_name").GetString() ?? ModelName, result.GetProperty("model_version").GetString() ?? ModelVersion, vector, ProviderName, ModelRevision, Preprocessing),
            result.GetProperty("width").GetInt32(), result.GetProperty("height").GetInt32(),
            result.GetProperty("perceptual_hash").GetString() ?? string.Empty);
    }
}
