using FileLens.Domain.Models;

namespace FileLens.Domain.Interfaces;

/// <summary>
/// Streams file metadata from an approved root directory safely.
/// Respects cancellation, ignores reparse points, and filters excluded paths.
/// </summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Traverses a root directory recursively and streams discovered file records.
    /// </summary>
    IAsyncEnumerable<IndexedFile> EnumerateFilesAsync(
        SearchRoot root,
        IReadOnlySet<string>? excludedFolders = null,
        IReadOnlySet<string>? excludedExtensions = null,
        Action<string, string>? onProgress = null,
        CancellationToken cancellationToken = default);
}

