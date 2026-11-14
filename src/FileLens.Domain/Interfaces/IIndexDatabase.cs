using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Data access abstraction for SQLite metadata storage.
/// </summary>
public interface IIndexDatabase
{
    /// <summary>
    /// Executes versioned migrations to ensure schema is up to date.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all configured search roots.
    /// </summary>
    Task<IReadOnlyList<SearchRoot>> GetAllRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single search root by identifier.
    /// </summary>
    Task<SearchRoot?> GetRootByIdAsync(SearchRootId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a search root matching the given normalized path.
    /// </summary>
    Task<SearchRoot?> GetRootByNormalizedPathAsync(string normalizedPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a newly configured search root.
    /// </summary>
    Task AddRootAsync(SearchRoot root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates status or configuration of an existing root.
    /// </summary>
    Task UpdateRootAsync(SearchRoot root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a root and cascades deletion of associated indexed file metadata.
    /// NEVER touches physical files on disk.
    /// </summary>
    Task DeleteRootAsync(SearchRootId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a batch of discovered file records in a single database transaction.
    /// </summary>
    Task UpsertFilesBatchAsync(IReadOnlyList<IndexedFile> files, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves indexed files for a root with paging.
    /// </summary>
    Task<IReadOnlyList<IndexedFile>> GetFilesByRootAsync(SearchRootId rootId, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts indexed files for a root.
    /// </summary>
    Task<int> GetFileCountByRootAsync(SearchRootId rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new indexing job execution.
    /// </summary>
    Task CreateJobAsync(IndexingJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates job status and completion timestamps.
    /// </summary>
    Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves current progress metrics for a job.
    /// </summary>
    Task SaveJobProgressAsync(JobProgress progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an error encountered during indexing.
    /// </summary>
    Task LogErrorAsync(IndexErrorRecord error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent errors for diagnostics.
    /// </summary>
    Task<IReadOnlyList<IndexErrorRecord>> GetRecentErrorsAsync(int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates system-wide indexing statistics.
    /// </summary>
    Task<IndexingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets any jobs left in Running or Paused state from an abnormal shutdown.
    /// </summary>
    Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default);
}

