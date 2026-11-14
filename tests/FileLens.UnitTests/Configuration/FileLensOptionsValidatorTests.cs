using FileLens.Application.Configuration;
using Microsoft.Extensions.Options;
using FileLens.UnitTests;

namespace FileLens.UnitTests.Configuration;

public sealed class FileLensOptionsValidatorTests
{
    private readonly FileLensOptionsValidator _validator = new();

    private static FileLensOptions CreateValidOptions() => new()
    {
        CacheDirectory = Path.Combine(Path.GetTempPath(), "FileLens_Test_Cache_" + Guid.NewGuid().ToString("N")),
        LogDirectory = Path.Combine(Path.GetTempPath(), "FileLens_Test_Logs_" + Guid.NewGuid().ToString("N")),
        MaxConcurrency = 4,
        MaxFileSizeBytes = 500 * 1024 * 1024
    };

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = CreateValidOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);

        // Clean up created directories
        CleanupDirectory(options.CacheDirectory);
        CleanupDirectory(options.LogDirectory);
    }

    [Fact]
    public void Validate_EmptyCacheDirectory_Fails()
    {
        var options = CreateValidOptions();
        options.CacheDirectory = "";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("CacheDirectory", result.FailureMessage);

        CleanupDirectory(options.LogDirectory);
    }

    [Fact]
    public void Validate_EmptyLogDirectory_Fails()
    {
        var options = CreateValidOptions();
        options.LogDirectory = "";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("LogDirectory", result.FailureMessage);

        CleanupDirectory(options.CacheDirectory);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(33)]
    [InlineData(100)]
    public void Validate_OutOfRangeConcurrency_Fails(int concurrency)
    {
        var options = CreateValidOptions();
        options.MaxConcurrency = concurrency;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxConcurrency", result.FailureMessage);

        CleanupDirectory(options.CacheDirectory);
        CleanupDirectory(options.LogDirectory);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(32)]
    public void Validate_ValidConcurrency_Succeeds(int concurrency)
    {
        var options = CreateValidOptions();
        options.MaxConcurrency = concurrency;

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);

        CleanupDirectory(options.CacheDirectory);
        CleanupDirectory(options.LogDirectory);
    }

    [Fact]
    public void Validate_NegativeMaxFileSize_Fails()
    {
        var options = CreateValidOptions();
        options.MaxFileSizeBytes = 0;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("MaxFileSizeBytes", result.FailureMessage);

        CleanupDirectory(options.CacheDirectory);
        CleanupDirectory(options.LogDirectory);
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

