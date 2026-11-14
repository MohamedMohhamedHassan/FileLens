using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Application.Services;

public sealed class IndexingJobEngine : IIndexingJobEngine, IDisposable
{
    private readonly IIndexDatabase _database;
    private readonly IFileDiscoveryService _discoveryService;
    private readonly ILogger<IndexingJobEngine> _logger;

    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private readonly ConcurrentDictionary<SearchRootId, Guid> _activeJobs = new();

    private CancellationTokenSource? _activeCts;
    private volatile bool _isPaused;
    private TaskCompletionSource<bool>? _pauseTcs;

    public bool IsRunning => _activeJobs.Count > 0;
    public bool IsPaused => _isPaused;

    public event EventHandler<JobProgress>? ProgressChanged;
    public event EventHandler<IndexErrorRecord>? ErrorOccurred;
    public event EventHandler<string>? LogMessage;

    public IndexingJobEngine(
        IIndexDatabase database,
        IFileDiscoveryService discoveryService,
        ILogger<IndexingJobEngine> logger)
    {
        _database = database;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    public async Task StartIndexingAsync(SearchRootId rootId, CancellationToken cancellationToken = default)
    {
        await _engineLock.WaitAsync(cancellationToken);
        try
        {
            if (_activeJobs.ContainsKey(rootId))
            {
                EmitLog($"Root {rootId} is already being indexed.");
                return;
            }

            var root = await _database.GetRootByIdAsync(rootId, cancellationToken);
            if (root == null || !root.IsEnabled)
            {
                EmitLog($"Root {rootId} is not found or is disabled.");
                return;
            }

            var jobId = Guid.NewGuid();
            _activeJobs.TryAdd(rootId, jobId);

            _activeCts ??= new CancellationTokenSource();
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_activeCts.Token, cancellationToken);

            _ = Task.Run(() => RunIndexJobForRootAsync(root, jobId, combinedCts.Token), CancellationToken.None);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    public async Task StartIndexingAllAsync(CancellationToken cancellationToken = default)
    {
        var roots = await _database.GetAllRootsAsync(cancellationToken);
        var enabledRoots = roots.Where(r => r.IsEnabled).ToList();

        if (enabledRoots.Count == 0)
        {
            EmitLog("No enabled search roots configured.");
            return;
        }

        foreach (var root in enabledRoots)
        {
            await StartIndexingAsync(root.Id, cancellationToken);
        }
    }

    public void PauseIndexing()
    {
        if (IsRunning && !_isPaused)
        {
            _isPaused = true;
            _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EmitLog("Indexing paused.");
        }
    }

    public void ResumeIndexing()
    {
        if (IsRunning && _isPaused)
        {
            _isPaused = false;
            _pauseTcs?.TrySetResult(true);
            _pauseTcs = null;
            EmitLog("Indexing resumed.");
        }
    }

    public void CancelIndexing()
    {
        if (IsRunning)
        {
            EmitLog("Cancelling active indexing operations...");
            // If paused, unpause first so worker tasks can observe cancellation
            if (_isPaused)
            {
                _isPaused = false;
                _pauseTcs?.TrySetResult(true);
            }

            _activeCts?.Cancel();
        }
    }

    public async Task RetryFailedJobsAsync(CancellationToken cancellationToken = default)
    {
        var errors = await _database.GetRecentErrorsAsync(100, cancellationToken);
        var distinctRootIds = errors.Select(e => e.RootId).Distinct().ToList();

        EmitLog($"Retrying indexing for {distinctRootIds.Count} roots with recorded errors...");
        foreach (var rootId in distinctRootIds)
        {
            await StartIndexingAsync(rootId, cancellationToken);
        }
    }

    private async Task RunIndexJobForRootAsync(SearchRoot root, Guid jobId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var job = new IndexingJob
        {
            Id = jobId,
            RootId = root.Id,
            JobType = JobType.FullScan,
            Status = JobStatus.Running,
            StartedAtUtc = DateTime.UtcNow
        };

        EmitLog($"Starting indexing for '{root.Path}' (JobId={jobId})...");
        await _database.CreateJobAsync(job, CancellationToken.None);

        root.Status = RootStatus.Indexing;
        await _database.UpdateRootAsync(root, CancellationToken.None);

        int discovered = 0;
        int processed = 0;
        int skipped = 0;
        int failed = 0;
        var batch = new List<IndexedFile>(100);

        try
        {
            await foreach (var file in _discoveryService.EnumerateFilesAsync(
                root,
                onProgress: (dir, fileName) =>
                {
                    // Fire progress snapshot to listeners
                    var snapshot = new JobProgress(jobId, root.Id, dir, fileName, discovered, processed, skipped, failed, DateTime.UtcNow);
                    ProgressChanged?.Invoke(this, snapshot);
                },
                cancellationToken: cancellationToken))
            {
                // Handle pause
                if (_isPaused && _pauseTcs != null)
                {
                    EmitLog($"Worker paused on directory: {Path.GetDirectoryName(file.FullPath)}");
                    await _pauseTcs.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();

                discovered++;
                var itemSnapshot = new JobProgress(jobId, root.Id, Path.GetDirectoryName(file.FullPath), file.FileName, discovered, processed, skipped, failed, DateTime.UtcNow);
                ProgressChanged?.Invoke(this, itemSnapshot);

                if (file.Status == FileStatus.Indexed)
                {
                    batch.Add(file);
                    processed++;
                }
                else if (file.Status == FileStatus.ReparsePointSkipped)
                {
                    skipped++;
                }
                else
                {
                    failed++;
                    var errorRecord = new IndexErrorRecord
                    {
                        JobId = jobId,
                        RootId = root.Id,
                        FilePath = file.FullPath,
                        ErrorCategory = file.Status.ToString(),
                        Message = $"File marked with non-indexed status: {file.Status}",
                        LoggedAtUtc = DateTime.UtcNow
                    };
                    await _database.LogErrorAsync(errorRecord, CancellationToken.None);
                    ErrorOccurred?.Invoke(this, errorRecord);
                }

                // Flush batch when threshold reached
                if (batch.Count >= 50)
                {
                    await _database.UpsertFilesBatchAsync(batch, CancellationToken.None);
                    batch.Clear();

                    var progress = new JobProgress(jobId, root.Id, Path.GetDirectoryName(file.FullPath), file.FileName, discovered, processed, skipped, failed, DateTime.UtcNow);
                    await _database.SaveJobProgressAsync(progress, CancellationToken.None);
                    ProgressChanged?.Invoke(this, progress);
                }
            }

            // Flush remaining files in batch
            if (batch.Count > 0)
            {
                await _database.UpsertFilesBatchAsync(batch, CancellationToken.None);
                batch.Clear();
            }

            stopwatch.Stop();
            job.Status = JobStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
            await _database.UpdateJobStatusAsync(jobId, JobStatus.Completed, null, CancellationToken.None);

            root.Status = RootStatus.Idle;
            root.LastScanUtc = DateTime.UtcNow;
            await _database.UpdateRootAsync(root, CancellationToken.None);

            var finalProgress = new JobProgress(jobId, root.Id, null, null, discovered, processed, skipped, failed, DateTime.UtcNow);
            await _database.SaveJobProgressAsync(finalProgress, CancellationToken.None);
            ProgressChanged?.Invoke(this, finalProgress);

            EmitLog($"Finished indexing '{root.Path}': {processed} files indexed, {failed} errors in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("Indexing cancelled for root {Path}", root.Path);
            job.Status = JobStatus.Cancelled;
            await _database.UpdateJobStatusAsync(jobId, JobStatus.Cancelled, "Operation cancelled by user", CancellationToken.None);

            root.Status = RootStatus.Idle;
            await _database.UpdateRootAsync(root, CancellationToken.None);

            EmitLog($"Indexing cancelled for '{root.Path}' after {processed} files processed.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error indexing root {Path}", root.Path);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            await _database.UpdateJobStatusAsync(jobId, JobStatus.Failed, ex.Message, CancellationToken.None);

            root.Status = RootStatus.Error;
            await _database.UpdateRootAsync(root, CancellationToken.None);

            var errorRecord = new IndexErrorRecord
            {
                JobId = jobId,
                RootId = root.Id,
                FilePath = root.Path,
                ErrorCategory = "UncaughtException",
                Message = ex.Message,
                LoggedAtUtc = DateTime.UtcNow
            };
            await _database.LogErrorAsync(errorRecord, CancellationToken.None);
            ErrorOccurred?.Invoke(this, errorRecord);

            EmitLog($"Error indexing '{root.Path}': {ex.Message}");
        }
        finally
        {
            _activeJobs.TryRemove(root.Id, out _);
            if (_activeJobs.IsEmpty)
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }
        }
    }

    private void EmitLog(string message)
    {
        _logger.LogInformation("{Message}", message);
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    public void Dispose()
    {
        _activeCts?.Dispose();
        _engineLock.Dispose();
    }
}

