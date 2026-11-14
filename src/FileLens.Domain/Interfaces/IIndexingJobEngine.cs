using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Background engine managing the lifecycle of indexing jobs across roots.
/// Supports starting, pausing, resuming, cancelling, retrying, and thread-safe progress reporting.
/// </summary>
public interface IIndexingJobEngine
{
    bool IsRunning { get; }
    bool IsPaused { get; }

    event EventHandler<JobProgress>? ProgressChanged;
    event EventHandler<IndexErrorRecord>? ErrorOccurred;
    event EventHandler<string>? LogMessage;

    Task StartIndexingAsync(SearchRootId rootId, CancellationToken cancellationToken = default);
    Task StartIndexingAllAsync(CancellationToken cancellationToken = default);
    void PauseIndexing();
    void ResumeIndexing();
    void CancelIndexing();
    Task RetryFailedJobsAsync(CancellationToken cancellationToken = default);
}

