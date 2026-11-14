using FileLens.Application.Services;
using FileLens.UnitTests;

namespace FileLens.UnitTests.Services;

public sealed class AiWorkerHealthCheckServiceTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy()
    {
        var service = new AiWorkerHealthCheckService();

        var result = await service.CheckHealthAsync();

        Assert.False(result.IsHealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsStubWorkerType()
    {
        var service = new AiWorkerHealthCheckService();

        var result = await service.CheckHealthAsync();

        Assert.Equal("Stub", result.WorkerType);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsPythonNotConfiguredMessage()
    {
        var service = new AiWorkerHealthCheckService();

        var result = await service.CheckHealthAsync();

        Assert.Equal("Python worker not configured", result.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsNullVersion()
    {
        var service = new AiWorkerHealthCheckService();

        var result = await service.CheckHealthAsync();

        Assert.Null(result.Version);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUtcTimestamp()
    {
        var before = DateTime.UtcNow;
        var service = new AiWorkerHealthCheckService();

        var result = await service.CheckHealthAsync();

        var after = DateTime.UtcNow;
        Assert.InRange(result.CheckedAtUtc, before, after);
    }

    [Fact]
    public async Task CheckHealthAsync_SupportsCancellation()
    {
        var service = new AiWorkerHealthCheckService();
        using var cts = new CancellationTokenSource();

        var result = await service.CheckHealthAsync(cts.Token);

        Assert.NotNull(result);
        Assert.False(result.IsHealthy);
    }
}

