using System.Diagnostics;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Application.Services;

public sealed class DocumentExtractionEngine : IDocumentExtractionEngine
{
    private readonly IIndexDatabase _database;
    private readonly IDocumentExtractionRegistry _registry;
    private readonly ILogger<DocumentExtractionEngine> _logger;

    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private CancellationTokenSource? _activeCts;
    private volatile bool _isRunning;

    public bool IsRunning => _isRunning;

    public event EventHandler<ExtractionProgress>? ProgressChanged;
    public event EventHandler<string>? LogMessage;

    public DocumentExtractionEngine(
        IIndexDatabase database,
        IDocumentExtractionRegistry registry,
        ILogger<DocumentExtractionEngine> logger)
    {
        _database = database;
        _registry = registry;
        _logger = logger;
    }

    public async Task RunExtractionAsync(SearchRootId? rootId = null, CancellationToken cancellationToken = default)
    {
        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isRunning)
            {
                EmitLog("Document extraction is already running.");
                return;
            }

            _isRunning = true;
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _activeCts.Token;

            EmitLog("Starting document extraction engine...");
            var stopwatch = Stopwatch.StartNew();

            int totalFiles = 0;
            int processed = 0;
            int extracted = 0;
            int failed = 0;
            int skipped = 0;

            const int batchSize = 100;

            while (!token.IsCancellationRequested)
            {
                var filesBatch = await _database.GetFilesAwaitingExtractionAsync(rootId, batchSize, token).ConfigureAwait(false);
                if (filesBatch.Count == 0)
                {
                    break;
                }

                totalFiles += filesBatch.Count;

                foreach (var file in filesBatch)
                {
                    token.ThrowIfCancellationRequested();

                    EmitProgress(new ExtractionProgress(
                        totalFiles,
                        processed,
                        extracted,
                        failed,
                        skipped,
                        file.FullPath,
                        DateTime.UtcNow));

                    try
                    {
                        var result = await ExtractFileAsync(file, token).ConfigureAwait(false);
                        processed++;

                        if (result.Status == ExtractionStatus.Extracted)
                        {
                            extracted++;
                        }
                        else if (result.Status is ExtractionStatus.UnsupportedFormat or ExtractionStatus.SkippedByPolicy)
                        {
                            skipped++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        processed++;
                        failed++;
                        _logger.LogError(ex, "Unexpected error extracting text for '{Path}'", file.FullPath);
                        EmitLog($"Error extracting '{file.FileName}': {ex.Message}");

                        await _database.LogErrorAsync(new IndexErrorRecord
                        {
                            Id = Guid.NewGuid(),
                            JobId = Guid.Empty,
                            RootId = file.RootId,
                            FilePath = file.FullPath,
                            ErrorCategory = "Extraction",
                            Message = ex.Message,
                            LoggedAtUtc = DateTime.UtcNow
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }

            stopwatch.Stop();
            EmitLog($"Document extraction completed in {stopwatch.ElapsedMilliseconds} ms. Processed: {processed}, Extracted: {extracted}, Failed: {failed}, Skipped: {skipped}.");

            EmitProgress(new ExtractionProgress(
                totalFiles,
                processed,
                extracted,
                failed,
                skipped,
                null,
                DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            EmitLog("Document extraction was cancelled.");
        }
        finally
        {
            _isRunning = false;
            _activeCts?.Dispose();
            _activeCts = null;
            _engineLock.Release();
        }
    }

    public async Task<ExtractedDocument> ExtractFileAsync(IndexedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var extractor = _registry.GetExtractor(file.Extension);
        if (extractor == null)
        {
            var unsupportedDoc = ExtractedDocument.CreateFailed(
                file.Id.Value,
                file.FullPath,
                "None",
                ExtractionStatus.UnsupportedFormat,
                $"Format '{file.Extension}' has no configured extractor adapter.");

            await _database.UpsertDocumentTextAsync(unsupportedDoc, cancellationToken).ConfigureAwait(false);
            return unsupportedDoc;
        }

        var result = await extractor.ExtractAsync(file, cancellationToken).ConfigureAwait(false);
        await _database.UpsertDocumentTextAsync(result, cancellationToken).ConfigureAwait(false);

        if (result.Status != ExtractionStatus.Extracted && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            await _database.LogErrorAsync(new IndexErrorRecord
            {
                Id = Guid.NewGuid(),
                JobId = Guid.Empty,
                RootId = file.RootId,
                FilePath = file.FullPath,
                ErrorCategory = "Extraction",
                Message = result.ErrorMessage,
                LoggedAtUtc = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public void Cancel()
    {
        if (_isRunning && _activeCts != null && !_activeCts.IsCancellationRequested)
        {
            EmitLog("Cancelling document extraction...");
            _activeCts.Cancel();
        }
    }

    private void EmitProgress(ExtractionProgress progress)
    {
        try
        {
            ProgressChanged?.Invoke(this, progress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error dispatching ExtractionProgress");
        }
    }

    private void EmitLog(string message)
    {
        _logger.LogInformation("[ExtractionEngine] {Message}", message);
        try
        {
            LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] [Extraction] {message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error dispatching LogMessage");
        }
    }
}
