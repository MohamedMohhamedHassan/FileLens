using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Application.Services;

/// <summary>
/// Stub implementation of <see cref="IAiWorkerHealthCheck"/> that reports
/// the Python worker as unconfigured when running in pure offline fallback mode.
/// </summary>
public sealed class AiWorkerHealthCheckService : IAiWorkerHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var result = new HealthCheckResult(
            IsHealthy: false,
            WorkerType: "Stub",
            Version: null,
            Message: "Python worker not configured",
            CheckedAtUtc: DateTime.UtcNow);

        return Task.FromResult(result);
    }
}
