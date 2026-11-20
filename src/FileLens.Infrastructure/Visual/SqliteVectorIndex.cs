using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Infrastructure.Visual;

public sealed class SqliteVectorIndex : IVectorIndex
{
    private readonly IIndexDatabase _database;
    public SqliteVectorIndex(IIndexDatabase database) => _database = database;

    public Task UpsertAsync(VisualEmbedding embedding, CancellationToken cancellationToken = default) => _database.UpsertVisualEmbeddingAsync(embedding, cancellationToken);
    public async Task RemoveAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var rows = await _database.GetVisualEmbeddingsAsync(cancellationToken);
        var row = rows.FirstOrDefault(x => x.Asset.Id == assetId);
        if (row.Asset is not null) await _database.RemoveVisualAssetAsync(row.Asset.FileId, cancellationToken);
    }

    public async Task<IReadOnlyList<VisualSearchResult>> SearchAsync(float[] queryVector, int limit, double threshold, string? providerName = null, string? modelName = null, string? modelVersion = null, CancellationToken cancellationToken = default)
    {
        var rows = await _database.GetVisualEmbeddingsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(providerName)) rows = rows.Where(x => x.Embedding.ProviderName == providerName && (string.IsNullOrWhiteSpace(modelName) || x.Embedding.ModelName == modelName) && (string.IsNullOrWhiteSpace(modelVersion) || x.Embedding.ModelVersion == modelVersion)).ToList();
        var results = rows.Select(row =>
        {
            var similarity = Cosine(queryVector, row.Embedding.Vector);
            var exact = row.Asset.ContentHash.Length > 0 && similarity >= 0.9999;
            var near = similarity >= 0.985;
            return new VisualSearchResult(row.Asset.Id, row.Asset.FileId, row.Asset.FullPath, Path.GetFileName(row.Asset.FullPath), similarity, exact ? "ExactDuplicate" : near ? "NearDuplicate" : "FeatureSimilarity", row.Asset.Width, row.Asset.Height, row.Asset.ContentHash, row.Asset.PerceptualHash);
        }).Where(x => x.Similarity >= threshold).OrderByDescending(x => x.Similarity).Take(limit).ToList();
        return results;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / (Math.Sqrt(aa) * Math.Sqrt(bb));
    }
}
