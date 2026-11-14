using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Checks the health and availability of the AI worker process.
/// </summary>
public interface IAiWorkerHealthCheck
{
    /// <summary>
    /// Performs a health check against the AI worker.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the check.</param>
    /// <returns>The health check result.</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

