using System;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// High-level orchestration service for OCR processing of standalone images and scanned PDFs.
/// </summary>
public interface IOcrService
{
    Task<OcrResult> ProcessImageAsync(string filePath, OcrOptions? options = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OcrResult>> ProcessPdfPagesAsync(string pdfPath, OcrOptions? options = null, CancellationToken cancellationToken = default);
    bool IsSupportedImageExtension(string extension);
}

