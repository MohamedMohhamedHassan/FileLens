using FileLens.Domain.Models;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.UnitTests.FileSystem;

public sealed class PathValidatorTests
{
    private readonly PathValidator _validator = new();

    [Fact]
    public void ValidatePath_EmptyPath_ReturnsFailure()
    {
        var result = _validator.ValidatePath("");
        Assert.False(result.IsValid);
        Assert.Equal("Path cannot be empty.", result.FailureReason);
    }

    [Fact]
    public void ValidatePath_WhitespacePath_ReturnsFailure()
    {
        var result = _validator.ValidatePath("   ");
        Assert.False(result.IsValid);
        Assert.Equal("Path cannot be empty.", result.FailureReason);
    }

    [Fact]
    public void ValidatePath_NonexistentDirectory_ReturnsFailure()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), "NonexistentFolder_" + Guid.NewGuid().ToString("N"));
        var result = _validator.ValidatePath(fakePath);
        Assert.False(result.IsValid);
        Assert.Contains("does not exist", result.FailureReason);
    }

    [Fact]
    public void ValidatePath_ValidLocalDirectory_ReturnsSuccess()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FileLens_ValidTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = _validator.ValidatePath(tempDir);
            Assert.True(result.IsValid);
            Assert.NotNull(result.CanonicalPath);
            Assert.Equal(RootType.LocalFolder, result.RootType);
            Assert.Equal(result.CanonicalPath!.ToLowerInvariant(), result.NormalizedPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DetectRootType_LocalFolder_ReturnsLocalFolder()
    {
        var type = _validator.DetectRootType(@"C:\Users\PC\Documents");
        Assert.Equal(RootType.LocalFolder, type);
    }

    [Fact]
    public void DetectRootType_LocalDriveRoot_ReturnsLocalDrive()
    {
        var type = _validator.DetectRootType(@"C:\");
        Assert.Equal(RootType.LocalDrive, type);
    }

    [Fact]
    public void DetectRootType_UncShare_ReturnsUncShare()
    {
        var type = _validator.DetectRootType(@"\\SERVER\SharedFolder");
        Assert.Equal(RootType.UncShare, type);
    }

    [Fact]
    public void DetectRootType_IpUncShare_ReturnsUncShare()
    {
        var type = _validator.DetectRootType(@"\\192.168.1.100\SharedFolder");
        Assert.Equal(RootType.UncShare, type);
    }

    [Fact]
    public void NormalizePath_RemovesTrailingSlashForNonRoot()
    {
        string normalized = _validator.NormalizePath(@"C:\Folder\Subfolder\");
        Assert.Equal(@"C:\Folder\Subfolder", normalized);
    }

    [Fact]
    public void NormalizePath_PreservesTrailingSlashForDriveRoot()
    {
        string normalized = _validator.NormalizePath(@"C:\");
        Assert.Equal(@"C:\", normalized);
    }

    [Fact]
    public void NormalizePath_ConvertsForwardSlashesInUnc()
    {
        string normalized = _validator.NormalizePath(@"//server/share/sub");
        Assert.Equal(@"\\server\share\sub", normalized);
    }

    [Fact]
    public void IsWithinRoot_ChildPath_ReturnsTrue()
    {
        bool within = _validator.IsWithinRoot(@"C:\Folder\Sub\file.txt", @"C:\Folder");
        Assert.True(within);
    }

    [Fact]
    public void IsWithinRoot_ExactRoot_ReturnsTrue()
    {
        bool within = _validator.IsWithinRoot(@"C:\Folder", @"C:\Folder");
        Assert.True(within);
    }

    [Fact]
    public void IsWithinRoot_SiblingPathWithSimilarPrefix_ReturnsFalse()
    {
        // "C:\FolderOther" should NOT be within "C:\Folder"
        bool within = _validator.IsWithinRoot(@"C:\FolderOther\file.txt", @"C:\Folder");
        Assert.False(within);
    }

    [Fact]
    public void IsWithinRoot_ParentPath_ReturnsFalse()
    {
        bool within = _validator.IsWithinRoot(@"C:\", @"C:\Folder");
        Assert.False(within);
    }
}

