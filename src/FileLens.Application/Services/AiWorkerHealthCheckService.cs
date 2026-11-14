using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Application.Services;

/// <summary>
/// Stub implementation of <see cref="IAiWorkerHealthCheck"/> that always reports
/// the Python worker as unavailable. This will be replaced when the Python AI
/// worker is implemented in Phase 3-4.
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

