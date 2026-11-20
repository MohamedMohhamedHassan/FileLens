using System.Security.Cryptography;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Visual;

public sealed class VisualIndexingService
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".webp", ".gif" };
    private readonly IIndexDatabase _database;
    private readonly IImageEmbeddingProvider _embeddings;
    private readonly ILogger<VisualIndexingService> _logger;
    public event EventHandler<VisualIndexProgress>? ProgressChanged;

    public VisualIndexingService(IIndexDatabase database, IImageEmbeddingProvider embeddings, ILogger<VisualIndexingService> logger)
        => (_database, _embeddings, _logger) = (database, embeddings, logger);

    public async Task IndexFilesAsync(IReadOnlyList<IndexedFile> files, CancellationToken cancellationToken = default)
    {
        var candidates = files.Where(x => Supported.Contains(x.Extension) && x.Status == FileStatus.Indexed).ToList();
        var processed = 0; var failed = 0;
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(file.FullPath) || new FileInfo(file.FullPath).Length > 100 * 1024 * 1024) throw new InvalidOperationException("Image is missing or exceeds the 100 MB safety limit.");
                var bytes = await File.ReadAllBytesAsync(file.FullPath, cancellationToken);
                var descriptor = await _embeddings.CreateEmbeddingAsync(bytes, cancellationToken);
                var assetId = DeterministicId(file.Id.Value);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                await _database.UpsertVisualAssetAsync(new VisualAsset(assetId, file.Id.Value, file.FullPath, file.Extension, descriptor.Width, descriptor.Height, file.SizeBytes, hash, descriptor.PerceptualHash, DateTime.UtcNow, "Ready"), cancellationToken);
                await _database.UpsertVisualEmbeddingAsync(new VisualEmbedding(assetId, descriptor.Embedding.ModelName, descriptor.Embedding.ModelVersion, descriptor.Embedding.Vector, DateTime.UtcNow, descriptor.Embedding.ProviderName, descriptor.Embedding.ModelRevision, descriptor.Embedding.Preprocessing), cancellationToken);
                processed++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { failed++; _logger.LogWarning(ex, "Visual indexing failed for {Path}", file.FullPath); }
            ProgressChanged?.Invoke(this, new VisualIndexProgress(processed, candidates.Count, file.FullPath, failed));
        }
    }

    private static Guid DeterministicId(Guid fileId) => GuidUtility.Create(fileId, "visual-asset");

    private static class GuidUtility
    {
        public static Guid Create(Guid namespaceId, string name)
        {
            var bytes = namespaceId.ToByteArray().Concat(System.Text.Encoding.UTF8.GetBytes(name)).ToArray();
            var hash = SHA256.HashData(bytes); Array.Resize(ref hash, 16); return new Guid(hash);
        }
    }
}
