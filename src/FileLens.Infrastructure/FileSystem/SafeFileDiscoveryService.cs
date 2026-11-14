using System.IO;
using System.Runtime.CompilerServices;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Infrastructure.FileSystem;

public sealed class SafeFileDiscoveryService : IFileDiscoveryService
{
    private readonly IPathValidator _pathValidator;
    private readonly ILogger<SafeFileDiscoveryService> _logger;

    private static readonly HashSet<string> DefaultExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        "System Volume Information",
        ".git",
        ".vs",
        "node_modules",
        "AppData"
    };

    private static readonly HashSet<string> DefaultExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp",
        ".lock",
        ".sys",
        ".swap"
    };

    public SafeFileDiscoveryService(IPathValidator pathValidator, ILogger<SafeFileDiscoveryService> logger)
    {
        _pathValidator = pathValidator;
        _logger = logger;
    }

    public async IAsyncEnumerable<IndexedFile> EnumerateFilesAsync(
        SearchRoot root,
        IReadOnlySet<string>? excludedFolders = null,
        IReadOnlySet<string>? excludedExtensions = null,
        Action<string, string>? onProgress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        var activeExcludedFolders = excludedFolders ?? DefaultExcludedFolders;
        var activeExcludedExtensions = excludedExtensions ?? DefaultExcludedExtensions;

        string rootCanonical = _pathValidator.NormalizePath(root.Path);
        if (!Directory.Exists(rootCanonical))
        {
            _logger.LogWarning("Search root path does not exist: {Path}", rootCanonical);
            yield break;
        }

        var directoryQueue = new Queue<string>();
        directoryQueue.Enqueue(rootCanonical);

        int counter = 0;

        while (directoryQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDir = directoryQueue.Dequeue();

            // Notify progress
            onProgress?.Invoke(currentDir, string.Empty);

            // Periodically yield control to keep pipeline responsive
            if (++counter % 50 == 0)
            {
                await Task.Yield();
            }

            // 1. Process files in current directory
            IEnumerable<string>? filePaths = null;
            IndexedFile? dirErrorRecord = null;
            try
            {
                filePaths = Directory.EnumerateFiles(currentDir);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogDebug("Access denied to directory: {Path} ({Message})", currentDir, ex.Message);
                dirErrorRecord = CreateSpecialFileRecord(root, currentDir, FileStatus.AccessDenied);
            }
            catch (Exception ex) when (ex is IOException or DirectoryNotFoundException)
            {
                _logger.LogWarning("I/O error reading directory: {Path} ({Message})", currentDir, ex.Message);
                dirErrorRecord = CreateSpecialFileRecord(root, currentDir, FileStatus.Error);
            }

            if (dirErrorRecord != null)
            {
                yield return dirErrorRecord;
                continue;
            }

            if (filePaths == null)
            {
                continue;
            }

            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Boundary verification: must remain within root
                if (!_pathValidator.IsWithinRoot(filePath, rootCanonical))
                {
                    _logger.LogWarning("File path escaped approved root: {Path}", filePath);
                    continue;
                }

                string ext = Path.GetExtension(filePath);
                if (activeExcludedExtensions.Contains(ext))
                {
                    continue;
                }

                // Check for reparse point (file symlink)
                if (_pathValidator.IsReparsePoint(filePath))
                {
                    _logger.LogDebug("Skipping file reparse point: {Path}", filePath);
                    continue;
                }

                IndexedFile? fileRecord = null;
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    string fileName = fileInfo.Name;
                    string relativePath = Path.GetRelativePath(rootCanonical, filePath);
                    string normalized = filePath.ToLowerInvariant();

                    fileRecord = new IndexedFile
                    {
                        Id = FileEntryId.New(),
                        RootId = root.Id,
                        FullPath = filePath,
                        NormalizedPath = normalized,
                        RelativePath = relativePath,
                        FileName = fileName,
                        Extension = ext,
                        SizeBytes = fileInfo.Length,
                        CreatedAtUtc = fileInfo.CreationTimeUtc,
                        ModifiedAtUtc = fileInfo.LastWriteTimeUtc,
                        Attributes = fileInfo.Attributes,
                        LastIndexedUtc = DateTime.UtcNow,
                        Status = FileStatus.Indexed
                    };

                    onProgress?.Invoke(currentDir, fileName);
                }
                catch (UnauthorizedAccessException)
                {
                    fileRecord = CreateSpecialFileRecord(root, filePath, FileStatus.AccessDenied);
                }
                catch (IOException)
                {
                    // Locked or in-use file: still record metadata if possible
                    fileRecord = CreateSpecialFileRecord(root, filePath, FileStatus.Locked);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error reading metadata for file {Path}: {Message}", filePath, ex.Message);
                    fileRecord = CreateSpecialFileRecord(root, filePath, FileStatus.Error);
                }

                if (fileRecord != null)
                {
                    yield return fileRecord;
                }
            }

            // 2. Discover subdirectories safely
            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(currentDir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogDebug("Cannot enumerate subdirectories in {Path}: {Message}", currentDir, ex.Message);
                continue;
            }

            foreach (var subDir in subDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Reparse point check: NEVER traverse directory junctions or symlinks
                if (_pathValidator.IsReparsePoint(subDir))
                {
                    _logger.LogDebug("Skipping directory junction/symlink: {Path}", subDir);
                    continue;
                }

                // Check directory name against excluded folders
                string dirName = Path.GetFileName(subDir);
                if (activeExcludedFolders.Contains(dirName))
                {
                    _logger.LogDebug("Skipping excluded folder: {Name}", dirName);
                    continue;
                }

                // Boundary verification: must remain within root
                if (!_pathValidator.IsWithinRoot(subDir, rootCanonical))
                {
                    _logger.LogWarning("Subdirectory escaped approved root: {Path}", subDir);
                    continue;
                }

                directoryQueue.Enqueue(subDir);
            }
        }
    }

    private static IndexedFile CreateSpecialFileRecord(SearchRoot root, string path, FileStatus status)
    {
        string fileName = Path.GetFileName(path);
        string rootCanonical = root.Path.TrimEnd('\\', '/');
        string relative = Path.GetRelativePath(rootCanonical, path);

        return new IndexedFile
        {
            Id = FileEntryId.New(),
            RootId = root.Id,
            FullPath = path,
            NormalizedPath = path.ToLowerInvariant(),
            RelativePath = relative,
            FileName = string.IsNullOrEmpty(fileName) ? path : fileName,
            Extension = Path.GetExtension(path),
            SizeBytes = 0,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow,
            Attributes = FileAttributes.Normal,
            LastIndexedUtc = DateTime.UtcNow,
            Status = status
        };
    }
}
