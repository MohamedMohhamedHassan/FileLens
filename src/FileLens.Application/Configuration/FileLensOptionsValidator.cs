using Microsoft.Extensions.Options;

namespace FileLens.Application.Configuration;

/// <summary>
/// Validates <see cref="FileLensOptions"/> at startup.
/// </summary>
public sealed class FileLensOptionsValidator : IValidateOptions<FileLensOptions>
{
    public ValidateOptionsResult Validate(string? name, FileLensOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CacheDirectory))
        {
            failures.Add("CacheDirectory must be specified.");
        }
        else if (!IsDirectoryAccessible(options.CacheDirectory))
        {
            failures.Add($"CacheDirectory '{options.CacheDirectory}' does not exist and cannot be created.");
        }

        if (string.IsNullOrWhiteSpace(options.LogDirectory))
        {
            failures.Add("LogDirectory must be specified.");
        }
        else if (!IsDirectoryAccessible(options.LogDirectory))
        {
            failures.Add($"LogDirectory '{options.LogDirectory}' does not exist and cannot be created.");
        }

        if (options.MaxConcurrency < 1 || options.MaxConcurrency > 32)
        {
            failures.Add($"MaxConcurrency must be between 1 and 32, but was {options.MaxConcurrency}.");
        }

        if (options.MaxFileSizeBytes < 1)
        {
            failures.Add($"MaxFileSizeBytes must be positive, but was {options.MaxFileSizeBytes}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Checks whether a directory exists or can be created.
    /// </summary>
    private static bool IsDirectoryAccessible(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            // Try to create the directory to verify it's a valid, writable path.
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }
}

