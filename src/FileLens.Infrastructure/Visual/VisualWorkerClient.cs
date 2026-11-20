using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.Visual;

/// <summary>Dedicated SigLIP worker client. It never launches the PaddleOCR environment.</summary>
public sealed class VisualWorkerClient : IDisposable
{
    private readonly string _python;
    private readonly string _script;
    private readonly string _model;
    private readonly ILogger<VisualWorkerClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;

    public VisualWorkerClient(ILogger<VisualWorkerClient> logger)
    {
        _logger = logger;
        _python = Environment.GetEnvironmentVariable("FILELENS_VISUAL_PYTHON") ??
            (File.Exists(@"D:\FileLens-AI\python\venv\Scripts\python.exe") ? @"D:\FileLens-AI\python\venv\Scripts\python.exe" : "python");
        _script = Environment.GetEnvironmentVariable("FILELENS_VISUAL_WORKER") ?? ResolveWorker();
        _model = Environment.GetEnvironmentVariable("FILELENS_SIGLIP_MODEL") ?? @"D:\FileLens-AI\models\siglip2-base-patch16-224";
    }

    public bool IsAvailable => File.Exists(_python) && File.Exists(_script) && Directory.Exists(_model);

    private static string ResolveWorker()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "src", "ai-worker", "worker.py");
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }
        return File.Exists(@"D:\FileLens\src\ai-worker\worker.py") ? @"D:\FileLens\src\ai-worker\worker.py" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "worker.py");
    }

    public Task<JsonElement?> PingAsync(CancellationToken cancellationToken = default) => SendAsync(new { command = "ping", id = Guid.NewGuid().ToString("N") }, TimeSpan.FromSeconds(15), cancellationToken);
    public Task<JsonElement?> HealthAsync(CancellationToken cancellationToken = default) => SendAsync(new { command = "health", id = Guid.NewGuid().ToString("N") }, TimeSpan.FromSeconds(15), cancellationToken);
    public Task<JsonElement?> ShutdownAsync(CancellationToken cancellationToken = default) => SendAsync(new { command = "shutdown", id = Guid.NewGuid().ToString("N") }, TimeSpan.FromSeconds(10), cancellationToken);

    public Task<JsonElement?> EmbedImageAsync(byte[] bytes, CancellationToken cancellationToken = default) =>
        SendAsync(new { command = "embed_image", id = Guid.NewGuid().ToString("N"), image_base64 = Convert.ToBase64String(bytes) }, TimeSpan.FromMinutes(2), cancellationToken);

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }) return;
            if (!IsAvailable) throw new InvalidOperationException($"SigLIP visual worker is unavailable. Python='{_python}', worker='{_script}', model='{_model}'.");
            var info = new ProcessStartInfo(_python)
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(_script)!
            };
            info.ArgumentList.Add(_script);
            info.Environment["FILELENS_WORKER_ROLE"] = "visual";
            info.Environment["FILELENS_SIGLIP_MODEL"] = _model;
            info.Environment["HF_HOME"] = Environment.GetEnvironmentVariable("HF_HOME") ?? @"D:\FileLens-AI\cache\huggingface";
            info.Environment["HF_HUB_CACHE"] = Environment.GetEnvironmentVariable("HF_HUB_CACHE") ?? @"D:\FileLens-AI\cache\huggingface\hub";
            info.Environment["TRANSFORMERS_OFFLINE"] = "1";
            info.Environment["HF_HUB_OFFLINE"] = "1";
            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            if (!_process.Start()) throw new InvalidOperationException("Failed to start SigLIP visual worker.");
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _logger.LogInformation("Started local SigLIP visual worker using {Python}", _python);
        }
        finally { _gate.Release(); }
    }

    private async Task<JsonElement?> SendAsync(object request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_stdin is null || _stdout is null) throw new InvalidOperationException("SigLIP worker streams are unavailable.");
            await _stdin.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var line = await _stdout.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException("SigLIP worker returned an empty response.");
            using var doc = JsonDocument.Parse(line);
            var result = doc.RootElement.Clone();
            if (result.TryGetProperty("status", out var status) && status.GetString() != "success")
                throw new InvalidOperationException(result.TryGetProperty("error", out var error) ? error.GetString() : "SigLIP worker request failed.");
            return result;
        }
        catch
        {
            await RestartAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task RestartAsync()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _process?.Dispose(); _process = null; _stdin = null; _stdout = null;
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _process?.Dispose(); _gate.Dispose();
    }
}
