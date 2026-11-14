using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using FileLens.Domain.Models;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.UnitTests.FileSystem;

public sealed class SafeFileDiscoveryServiceTests
{
    private readonly PathValidator _validator = new();
    private readonly SafeFileDiscoveryService _service;
    private readonly string _testDataDir = @"d:\FileLens\testdata";

    public SafeFileDiscoveryServiceTests()
    {
        _service = new SafeFileDiscoveryService(_validator, NullLogger<SafeFileDiscoveryService>.Instance);
    }

    [Fact]
    public async Task EnumerateFilesAsync_DiscoversSyntheticTestDataFiles()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };

        var files = new List<IndexedFile>();
        await foreach (var file in _service.EnumerateFilesAsync(root))
        {
            files.Add(file);
        }

        Assert.True(files.Count >= 8, $"Expected at least 8 files, found {files.Count}");

        // Verify Arabic and Unicode filenames are discovered correctly
        bool foundArabic = files.Any(f => f.FileName.Contains("مستند_عربي") || f.FileName.Contains("تقرير_2026"));
        Assert.True(foundArabic, "Should have discovered Arabic named files");

        bool foundFrench = files.Any(f => f.FileName.Contains("résumé"));
        Assert.True(foundFrench, "Should have discovered French named files");
    }

    [Fact]
    public async Task EnumerateFilesAsync_ExcludesConfiguredFolders()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };

        var files = new List<IndexedFile>();
        await foreach (var file in _service.EnumerateFilesAsync(root))
        {
            files.Add(file);
        }

        // Verify files inside excluded folders (.git, node_modules) were NOT yielded
        Assert.False(files.Any(f => f.FullPath.Contains(".git")), ".git folder files must be excluded");
        Assert.False(files.Any(f => f.FullPath.Contains("node_modules")), "node_modules folder files must be excluded");
    }

    [Fact]
    public async Task EnumerateFilesAsync_ExcludesConfiguredExtensions()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };

        var excludedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tmp" };

        var files = new List<IndexedFile>();
        await foreach (var file in _service.EnumerateFilesAsync(root, excludedExtensions: excludedExts))
        {
            files.Add(file);
        }

        Assert.False(files.Any(f => f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)), ".tmp files should be excluded");
    }

    [Fact]
    public async Task EnumerateFilesAsync_Cancellation_StopsEnumeration()
    {
        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = _testDataDir,
            NormalizedPath = _testDataDir.ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        bool threw = false;
        try
        {
            await foreach (var _ in _service.EnumerateFilesAsync(root, cancellationToken: cts.Token))
            {
            }
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert.True(threw, "OperationCanceledException should be thrown when cancelled.");
    }

    [Fact]
    public async Task EnumerateFilesAsync_NeverModifiesSourceFiles()
    {
        string sampleFile = Path.Combine(_testDataDir, "docs", "readme.txt");
        byte[] originalHash = ComputeSha256(sampleFile);
        var originalModTime = File.GetLastWriteTimeUtc(sampleFile);

        var root = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = Path.Combine(_testDataDir, "docs"),
            NormalizedPath = Path.Combine(_testDataDir, "docs").ToLowerInvariant(),
            RootType = RootType.LocalFolder,
            IsEnabled = true
        };

        await foreach (var _ in _service.EnumerateFilesAsync(root))
        {
            // Just enumerate
        }

        // Verify SHA256 and modification time remain IDENTICAL
        byte[] afterHash = ComputeSha256(sampleFile);
        var afterModTime = File.GetLastWriteTimeUtc(sampleFile);

        Assert.Equal(Convert.ToBase64String(originalHash), Convert.ToBase64String(afterHash));
        Assert.Equal(originalModTime, afterModTime);
    }

    private static byte[] ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return SHA256.HashData(stream);
    }
}

