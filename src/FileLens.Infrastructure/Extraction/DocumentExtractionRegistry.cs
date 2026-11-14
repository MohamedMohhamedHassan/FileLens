using FileLens.Domain.Interfaces;

namespace FileLens.Infrastructure.Extraction;

public sealed class DocumentExtractionRegistry : IDocumentExtractionRegistry
{
    private readonly Dictionary<string, IDocumentExtractor> _extensionMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDocumentExtractor> _extractors = new();

    public DocumentExtractionRegistry(IEnumerable<IDocumentExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);

        foreach (var extractor in extractors)
        {
            _extractors.Add(extractor);
            foreach (var ext in extractor.SupportedExtensions)
            {
                var normalized = ext.StartsWith('.') ? ext : "." + ext;
                _extensionMap[normalized] = extractor;
            }
        }
    }

    public IDocumentExtractor? GetExtractor(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var ext = extension.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return _extensionMap.TryGetValue(ext, out var extractor) ? extractor : null;
    }

    public bool IsSupported(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var ext = extension.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return _extensionMap.ContainsKey(ext);
    }

    public IReadOnlyList<IDocumentExtractor> GetAllExtractors() => _extractors;
}

