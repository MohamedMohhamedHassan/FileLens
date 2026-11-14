using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Ocr;

/// <summary>
/// Communicates with the local Python AI worker process over stdin/stdout using JSON-lines.
/// Handles lifecycle management, process crash recovery, timeouts, and request validation.
/// </summary>
public sealed class OcrWorkerClient : IDisposable
{
    private readonly string _pythonExecutable;
    private readonly string _workerScriptPath;
    private readonly ILogger<OcrWorkerClient> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _isDisposed;

    public OcrWorkerClient(
        ILogger<OcrWorkerClient> logger,
        string? pythonExecutable = null,
        string? workerScriptPath = null)
    {
        _logger = logger;
        _pythonExecutable = pythonExecutable ?? ResolvePythonPath();
        _workerScriptPath = workerScriptPath ?? ResolveWorkerScriptPath();
    }

    public bool IsWorkerAvailable => File.Exists(_pythonExecutable) && File.Exists(_workerScriptPath);

    private static string ResolvePythonPath()
    {
        // 1. Check local virtual environment in src/ai-worker/.venv
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var relativeVenv = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "ai-worker", ".venv", "Scripts", "python.exe"));
        if (File.Exists(relativeVenv)) return relativeVenv;

        // 2. Fall back to standard python on PATH
        return "python";
    }

    private static string ResolveWorkerScriptPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var relativeScript = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "ai-worker", "worker.py"));
        if (File.Exists(relativeScript)) return relativeScript;

        return Path.Combine(baseDir, "ai-worker", "worker.py");
    }

    public async Task<bool> EnsureWorkerStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_process != null && !_process.HasExited)
        {
            return true;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_process != null && !_process.HasExited)
            {
                return true;
            }

            if (!IsWorkerAvailable)
            {
                _logger.LogWarning("Python AI worker not available at '{PythonPath}'. Local fallback OCR will be used.", _pythonExecutable);
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonExecutable,
                Arguments = $"\"{_workerScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _logger.LogInformation("Starting AI Worker process: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);
            _process = new Process { StartInfo = startInfo };
            _process.Start();

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch Python AI worker process.");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<JsonElement?> SendCommandAsync(object requestPayload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        bool started = await EnsureWorkerStartedAsync(cancellationToken);
        if (!started || _stdin == null || _stdout == null)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            string jsonLine = JsonSerializer.Serialize(requestPayload);
            await _stdin.WriteLineAsync(jsonLine.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var readTask = _stdout.ReadLineAsync(cts.Token).AsTask();
            string? responseLine = await readTask;

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                _logger.LogWarning("AI worker returned empty response.");
                return null;
            }

            using var doc = JsonDocument.Parse(responseLine);
            return doc.RootElement.Clone();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI worker request timed out after {TimeoutMs}ms.", timeout.TotalMilliseconds);
            await RestartWorkerAsync();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AI worker IPC communication.");
            await RestartWorkerAsync();
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task RestartWorkerAsync()
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        finally
        {
            _process = null;
            _stdin = null;
            _stdout = null;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _stdin?.WriteLine("{\"command\":\"shutdown\"}");
                _process.WaitForExit(1000);
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _lock.Dispose();
        }
    }
}

