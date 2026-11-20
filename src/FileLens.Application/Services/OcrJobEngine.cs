using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Application.Services;

public sealed class OcrJobEngine : IOcrJobEngine
{
    private readonly IIndexDatabase _database;
    private readonly IOcrService _ocrService;
    private readonly ILogger<OcrJobEngine> _logger;

    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private CancellationTokenSource? _jobCts;
    private Task? _runningTask;
    private volatile bool _isPaused;

    public bool IsRunning => _runningTask != null && !_runningTask.IsCompleted;
    public bool IsPaused => _isPaused;
    public int ProcessedCount { get; private set; }
    public int FailedCount { get; private set; }

    public event EventHandler<OcrStatistics>? StatisticsUpdated;
    public event EventHandler<OcrJobProgress>? ProgressChanged;
    public event EventHandler<string>? LogMessage;

    public OcrJobEngine(
        IIndexDatabase database,
        IOcrService ocrService,
        ILogger<OcrJobEngine> logger)
    {
        _database = database;
        _ocrService = ocrService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _engineLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                _logger.LogWarning("OCR job engine is already running.");
                return;
            }

            _jobCts = new CancellationTokenSource();
            _isPaused = false;
            _runningTask = Task.Run(() => RunOcrPipelineAsync(_jobCts.Token), _jobCts.Token);
        }
        finally
        {
            _engineLock.Release();
        }
    }

    public async Task RunOcrPipelineAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting OCR processing pipeline...");
        ProcessedCount = 0;
        FailedCount = 0;

        try
        {
            LogMessage?.Invoke(this, "[OCR] Starting OCR scan. Checking queued images and scanned documents...");
            var files = await _database.GetFilesAwaitingOcrAsync(1000, cancellationToken);
            _logger.LogInformation("Found {Count} image/scanned files awaiting OCR.", files.Count);

            if (files.Count == 0)
            {
                LogMessage?.Invoke(this, "[OCR] No new images or scanned documents awaiting OCR.");
                ProgressChanged?.Invoke(this, new OcrJobProgress(0, 0, null));
                return;
            }

            LogMessage?.Invoke(this, $"[OCR] Found {files.Count} files awaiting OCR. Beginning recognition...");
            int total = files.Count;
            int current = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                while (_isPaused)
                {
                    await Task.Delay(200, cancellationToken);
                }

                current++;
                ProgressChanged?.Invoke(this, new OcrJobProgress(current, total, file.FullPath));
                LogMessage?.Invoke(this, $"[OCR] ({current}/{total}) Processing: {file.FileName}");

                if (!File.Exists(file.FullPath))
                {
                    _logger.LogWarning("File not found on disk during OCR: {Path}", file.FullPath);
                    continue;
                }

                // Check file size limit: skip files > 50 MB to prevent decompression bombs
                var fileInfo = new FileInfo(file.FullPath);
                if (fileInfo.Length > 50 * 1024 * 1024)
                {
                    _logger.LogWarning("File exceeds OCR size safety threshold (50 MB): {Path}", file.FullPath);
                    var skipRecord = new OcrRecord
                    {
                        FileId = file.Id.Value,
                        EngineName = "SafetyCheck",
                        ModelName = "None",
                        ModelVersion = "1.0",
                        Language = "None",
                        Confidence = 0.0,
                        RawText = string.Empty,
                        NormalizedText = string.Empty,
                        ProcessingTimeMs = 0,
                        ErrorMessage = "File size exceeds 50 MB threshold"
                    };
                    await _database.UpsertOcrResultAsync(skipRecord, cancellationToken);
                    FailedCount++;
                    continue;
                }

                try
                {
                    OcrResult res;
                    if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        var pdfResults = await _ocrService.ProcessPdfPagesAsync(file.FullPath, cancellationToken: cancellationToken);
                        foreach (var pageRes in pdfResults)
                        {
                            var ocrRec = CreateRecord(file.Id.Value, pageRes);
                            await _database.UpsertOcrResultAsync(ocrRec, cancellationToken);
                        }
                        ProcessedCount++;
                        continue;
                    }
                    else
                    {
                        res = await _ocrService.ProcessImageAsync(file.FullPath, cancellationToken: cancellationToken);
                    }

                    if (res.IsSuccess)
                    {
                        var record = CreateRecord(file.Id.Value, res);
                        await _database.UpsertOcrResultAsync(record, cancellationToken);
                        ProcessedCount++;
                    }
                    else
                    {
                        var failRecord = new OcrRecord
                        {
                            FileId = file.Id.Value,
                            EngineName = res.Engine,
                            ModelName = res.ModelName,
                            ModelVersion = res.ModelVersion,
                            Language = res.Language,
                            Confidence = 0.0,
                            RawText = string.Empty,
                            NormalizedText = string.Empty,
                            ProcessingTimeMs = res.ProcessingTimeMs,
                            ErrorMessage = res.ErrorMessage ?? "OCR failed"
                        };
                        await _database.UpsertOcrResultAsync(failRecord, cancellationToken);
                        FailedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OCR processing error for file '{Path}'.", file.FullPath);
                    FailedCount++;
                    var errorRecord = new OcrRecord
                    {
                        FileId = file.Id.Value,
                        EngineName = "EngineError",
                        ModelName = "None",
                        ModelVersion = "1.0",
                        Language = "None",
                        Confidence = 0.0,
                        RawText = string.Empty,
                        NormalizedText = string.Empty,
                        ProcessingTimeMs = 0,
                        ErrorMessage = ex.Message
                    };
                    await _database.UpsertOcrResultAsync(errorRecord, cancellationToken);
                }

                // Periodically update statistics
                if ((ProcessedCount + FailedCount) % 5 == 0)
                {
                    var stats = await _database.GetOcrStatisticsAsync(cancellationToken);
                    StatisticsUpdated?.Invoke(this, stats);
                }
            }

            var finalStats = await _database.GetOcrStatisticsAsync(cancellationToken);
            StatisticsUpdated?.Invoke(this, finalStats);
            ProgressChanged?.Invoke(this, new OcrJobProgress(total, total, null));
            LogMessage?.Invoke(this, $"[OCR] Finished. {ProcessedCount} processed, {FailedCount} skipped/failed.");
            _logger.LogInformation("OCR pipeline completed. Processed: {P}, Failed: {F}", ProcessedCount, FailedCount);
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke(this, "[OCR] Operation cancelled by user.");
            _logger.LogInformation("OCR pipeline was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in OCR pipeline.");
        }
    }

    public void Pause()
    {
        _isPaused = true;
        _logger.LogInformation("OCR engine paused.");
    }

    public void Resume()
    {
        _isPaused = false;
        _logger.LogInformation("OCR engine resumed.");
    }

    public void Cancel()
    {
        _jobCts?.Cancel();
        _isPaused = false;
        _logger.LogInformation("OCR engine cancel requested.");
    }

    public async Task RetryFailedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrying failed OCR files...");
        await RunOcrPipelineAsync(cancellationToken);
    }

    private static OcrRecord CreateRecord(Guid fileId, OcrResult res)
    {
        string? regionsJson = null;
        if (res.Lines.Count > 0)
        {
            try
            {
                regionsJson = JsonSerializer.Serialize(res.Lines);
            }
            catch { }
        }

        return new OcrRecord
        {
            FileId = fileId,
            EngineName = res.Engine,
            ModelName = res.ModelName,
            ModelVersion = res.ModelVersion,
            Language = res.Language,
            PageNumber = res.PageNumber,
            Confidence = res.Confidence,
            RawText = res.RawText,
            NormalizedText = res.NormalizedText,
            RegionsJson = regionsJson,
            ProcessingTimeMs = res.ProcessingTimeMs,
            ExtractedAtUtc = DateTime.UtcNow,
            ErrorMessage = res.ErrorMessage
        };
    }
}

