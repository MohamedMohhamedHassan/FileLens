using System.ComponentModel.DataAnnotations;

namespace FileLens.Application.Configuration;

/// <summary>
/// Configuration options for the FileLens application.
/// </summary>
public sealed class FileLensOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "FileLens";

    /// <summary>
    /// Directory for storing cache data (thumbnails, previews, extracted content).
    /// </summary>
    [Required]
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Directory for storing application log files.
    /// </summary>
    [Required]
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of concurrent indexing operations.
    /// Must be between 1 and 32.
    /// </summary>
    [Range(1, 32)]
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Maximum file size in bytes to process for text extraction.
    /// Default: 500 MB.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long MaxFileSizeBytes { get; set; } = 500 * 1024 * 1024;
}

