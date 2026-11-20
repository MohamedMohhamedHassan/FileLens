using System;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

public interface IOcrJobEngine
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    int ProcessedCount { get; }
    int FailedCount { get; }

    event EventHandler<OcrStatistics>? StatisticsUpdated;
    event EventHandler<OcrJobProgress>? ProgressChanged;
    event EventHandler<string>? LogMessage;

    Task RunOcrPipelineAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    void Pause();
    void Resume();
    void Cancel();
    Task RetryFailedAsync(CancellationToken cancellationToken = default);
}

