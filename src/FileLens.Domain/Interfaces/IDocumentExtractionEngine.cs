using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

public sealed record ExtractionProgress(
    int TotalFiles,
    int ProcessedFiles,
    int ExtractedCount,
    int FailedCount,
    int SkippedCount,
    string? CurrentFile,
    DateTime TimestampUtc);

/// <summary>
/// Background engine coordinating document text extraction across indexed files.
/// </summary>
public interface IDocumentExtractionEngine
{
    bool IsRunning { get; }
    Task RunExtractionAsync(SearchRootId? rootId = null, CancellationToken cancellationToken = default);
    Task<ExtractedDocument> ExtractFileAsync(IndexedFile file, CancellationToken cancellationToken = default);
    void Cancel();

    event EventHandler<ExtractionProgress>? ProgressChanged;
    event EventHandler<string>? LogMessage;
}

