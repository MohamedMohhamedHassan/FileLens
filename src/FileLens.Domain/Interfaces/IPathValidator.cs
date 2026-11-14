using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Validates, normalizes, and classifies candidate search root paths.
/// Ensures path safety, prevents traversal outside roots, and detects reparse points.
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Validates that a raw path is well-formed, exists, is readable, and is a supported root type.
    /// </summary>
    PathValidationResult ValidatePath(string rawPath);

    /// <summary>
    /// Produces a canonical full path formatted for Windows.
    /// </summary>
    string NormalizePath(string rawPath);

    /// <summary>
    /// Detects whether a path refers to a local folder, full drive, or UNC network share.
    /// </summary>
    RootType DetectRootType(string path);

    /// <summary>
    /// Checks whether the target file or directory is an NTFS reparse point (symbolic link or junction).
    /// </summary>
    bool IsReparsePoint(string path);

    /// <summary>
    /// Verifies that a target path is strictly located within an approved root directory.
    /// </summary>
    bool IsWithinRoot(string targetPath, string rootPath);
}

