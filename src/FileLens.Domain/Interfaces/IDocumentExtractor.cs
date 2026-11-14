using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Parser adapter for a specific file format family (e.g. DOCX, PDF, XLSX, TXT).
/// </summary>
public interface IDocumentExtractor
{
    string ExtractorId { get; }
    IReadOnlySet<string> SupportedExtensions { get; }

    bool CanExtract(string filePath, string extension);
    Task<ExtractedDocument> ExtractAsync(IndexedFile file, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry of all configured document extraction adapters.
/// </summary>
public interface IDocumentExtractionRegistry
{
    IDocumentExtractor? GetExtractor(string extension);
    bool IsSupported(string extension);
    IReadOnlyList<IDocumentExtractor> GetAllExtractors();
}

