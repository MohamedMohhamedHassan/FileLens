using System.IO;
using System.Text.RegularExpressions;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Infrastructure.FileSystem;

public sealed partial class PathValidator : IPathValidator
{
    // Regex for UNC share pattern: \\<server-or-ip>\<share>[\<subfolder>]
    [GeneratedRegex(@"^\\\\[a-zA-Z0-9_\-\.]{1,255}\\[a-zA-Z0-9_\-\.\$\s]+", RegexOptions.Compiled)]
    private static partial Regex UncRegex();

    public PathValidationResult ValidatePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return PathValidationResult.Failure("Path cannot be empty.");
        }

        string trimmed = rawPath.Trim();

        // Check for invalid path characters
        char[] invalidChars = Path.GetInvalidPathChars();
        if (trimmed.IndexOfAny(invalidChars) >= 0)
        {
            return PathValidationResult.Failure("Path contains invalid characters.");
        }

        string canonical;
        try
        {
            canonical = NormalizePath(trimmed);
        }
        catch (Exception ex)
        {
            return PathValidationResult.Failure($"Invalid path syntax: {ex.Message}");
        }

        var rootType = DetectRootType(canonical);

        // Verify existence and read permissions
        try
        {
            if (!Directory.Exists(canonical))
            {
                return PathValidationResult.Failure(
                    rootType == RootType.UncShare
                        ? $"Network share '{canonical}' is unreachable or does not exist."
                        : $"Directory '{canonical}' does not exist.");
            }

            // Test read access by enumerating 1 item
            _ = Directory.EnumerateFileSystemEntries(canonical).FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return PathValidationResult.Failure($"Access denied: Current user does not have read permissions for '{canonical}'.");
        }
        catch (IOException ex)
        {
            return PathValidationResult.Failure($"I/O error accessing '{canonical}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return PathValidationResult.Failure($"Unable to access '{canonical}': {ex.Message}");
        }

        string normalized = canonical.ToLowerInvariant();
        return PathValidationResult.Success(canonical, normalized, rootType);
    }

    public string NormalizePath(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);
        string trimmed = rawPath.Trim();

        // Check if it's a UNC path
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            // Standardize slashes to backslashes
            string uniform = trimmed.Replace('/', '\\');
            // Remove redundant trailing slashes
            while (uniform.Length > 2 && uniform.EndsWith('\\'))
            {
                uniform = uniform[..^1];
            }
            return uniform;
        }

        string full = Path.GetFullPath(trimmed);
        // If it's a drive root like "C:\", keep the trailing slash; otherwise trim it
        string? root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return full.EndsWith('\\') ? full : full + "\\";
        }

        return full.TrimEnd('\\', '/');
    }

    public RootType DetectRootType(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return RootType.UncShare;
        }

        string? root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return RootType.LocalDrive;
        }

        return RootType.LocalFolder;
    }

    public bool IsReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch
        {
            // If attributes cannot be read, treat safely
        }
        return false;
    }

    public bool IsWithinRoot(string targetPath, string rootPath)
    {
        try
        {
            string normTarget = NormalizePath(targetPath);
            string normRoot = NormalizePath(rootPath);

            if (string.Equals(normTarget, normRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string prefix = normRoot.EndsWith('\\') ? normRoot : normRoot + "\\";
            return normTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

