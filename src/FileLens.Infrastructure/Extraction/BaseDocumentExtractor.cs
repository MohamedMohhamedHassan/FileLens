using System.Diagnostics;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Extraction;

public abstract class BaseDocumentExtractor : IDocumentExtractor
{
    protected readonly ITextNormalizer Normalizer;
    protected readonly ILogger Logger;
    public const long DefaultMaxFileSize = 100 * 1024 * 1024; // 100 MB safe limit

    protected BaseDocumentExtractor(ITextNormalizer normalizer, ILogger logger)
    {
        Normalizer = normalizer;
        Logger = logger;
    }

    public abstract string ExtractorId { get; }
    public abstract IReadOnlySet<string> SupportedExtensions { get; }

    public virtual bool CanExtract(string filePath, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return SupportedExtensions.Contains(ext);
    }

    public async Task<ExtractedDocument> ExtractAsync(IndexedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!File.Exists(file.FullPath))
        {
            return ExtractedDocument.CreateFailed(
                file.Id.Value,
                file.FullPath,
                ExtractorId,
                ExtractionStatus.Failed,
                "File not found on disk");
        }

        var fileInfo = new FileInfo(file.FullPath);
        if (fileInfo.Length > DefaultMaxFileSize)
        {
            Logger.LogWarning("File '{Path}' ({Size} bytes) exceeds maximum extraction size limit of {Limit} bytes",
                file.FullPath, fileInfo.Length, DefaultMaxFileSize);
            return ExtractedDocument.CreateFailed(
                file.Id.Value,
                file.FullPath,
                ExtractorId,
                ExtractionStatus.TooLarge,
                $"File exceeds maximum limit of {DefaultMaxFileSize / (1024 * 1024)} MB");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ExtractContentAsync(file, fileInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            Logger.LogWarning(ex, "Corrupt document format in '{Path}'", file.FullPath);
            return ExtractedDocument.CreateFailed(file.Id.Value, file.FullPath, ExtractorId, ExtractionStatus.Corrupt, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Extraction failed for '{Path}' using {ExtractorId}", file.FullPath, ExtractorId);
            return ExtractedDocument.CreateFailed(file.Id.Value, file.FullPath, ExtractorId, ExtractionStatus.Failed, ex.Message);
        }
    }

    protected abstract Task<ExtractedDocument> ExtractContentAsync(
        IndexedFile file,
        FileInfo fileInfo,
        CancellationToken cancellationToken);

    protected DocumentChunk CreateChunk(
        Guid fileId,
        int chunkIndex,
        string locationType,
        string locationReference,
        string rawText,
        int? pageNumber = null,
        string? sheetName = null,
        string? cellAddress = null,
        int? slideNumber = null)
    {
        var normalized = Normalizer.NormalizeText(rawText);
        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            ChunkIndex = chunkIndex,
            LocationType = locationType,
            LocationReference = locationReference,
            PageNumber = pageNumber,
            SheetName = sheetName,
            CellAddress = cellAddress,
            SlideNumber = slideNumber,
            RawText = rawText,
            NormalizedText = normalized
        };
    }
}

