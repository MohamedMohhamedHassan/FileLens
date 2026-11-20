using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Infrastructure.Ocr;

/// <summary>
/// Production health check service that queries the live Python AI worker process via IPC.
/// </summary>
public sealed class ProductionAiWorkerHealthCheckService : IAiWorkerHealthCheck
{
    private readonly OcrWorkerClient _workerClient;

    public ProductionAiWorkerHealthCheckService(OcrWorkerClient workerClient)
    {
        _workerClient = workerClient;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!_workerClient.IsWorkerAvailable)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                WorkerType: "None",
                Version: null,
                Message: "Python worker not available",
                CheckedAtUtc: DateTime.UtcNow);
        }

        try
        {
            var resp = await _workerClient.HealthAsync(cancellationToken);
            if (resp.HasValue && resp.Value.TryGetProperty("is_healthy", out var healthyProp) && healthyProp.GetBoolean())
            {
                string engine = resp.Value.TryGetProperty("engine", out var eng) ? eng.GetString() ?? "PaddleOCR" : "PaddleOCR";
                string ver = resp.Value.TryGetProperty("version", out var v) ? v.GetString() ?? "3.7.0" : "3.7.0";
                bool production = resp.Value.TryGetProperty("production_engine", out var prod) && prod.GetBoolean()
                    && engine.StartsWith("PaddleOCR/", StringComparison.OrdinalIgnoreCase);
                return new HealthCheckResult(
                    IsHealthy: production,
                    WorkerType: engine,
                    Version: ver,
                    Message: production ? "Online and healthy: production PaddleOCR" : "Worker online, but production PaddleOCR is unavailable; fallback is active",
                    CheckedAtUtc: DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                WorkerType: "Error",
                Version: null,
                Message: ex.Message,
                CheckedAtUtc: DateTime.UtcNow);
        }

        return new HealthCheckResult(
            IsHealthy: false,
            WorkerType: "Unknown",
            Version: null,
            Message: "Python worker returned unhealthy response",
            CheckedAtUtc: DateTime.UtcNow);
    }
}

