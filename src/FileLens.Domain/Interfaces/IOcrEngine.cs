using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Performs OCR on images or scanned pages with language and layout options.
/// </summary>
public interface IOcrEngine
{
    string EngineName { get; }
    string ModelVersion { get; }

    Task<OcrResult> ExtractTextAsync(string filePath, OcrOptions options, CancellationToken cancellationToken = default);
    Task<OcrResult> ExtractTextFromBytesAsync(byte[] imageBytes, OcrOptions options, CancellationToken cancellationToken = default);
}

