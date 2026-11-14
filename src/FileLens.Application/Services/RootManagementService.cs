using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FileLens.Application.Services;

public sealed class RootManagementService
{
    private readonly IIndexDatabase _database;
    private readonly IPathValidator _pathValidator;
    private readonly ILogger<RootManagementService> _logger;

    public RootManagementService(
        IIndexDatabase database,
        IPathValidator pathValidator,
        ILogger<RootManagementService> logger)
    {
        _database = database;
        _pathValidator = pathValidator;
        _logger = logger;
    }

    public async Task<PathValidationResult> ValidateAndAddRootAsync(string rawPath, CancellationToken cancellationToken = default)
    {
        var validation = _pathValidator.ValidatePath(rawPath);
        if (!validation.IsValid || validation.CanonicalPath == null || validation.NormalizedPath == null)
        {
            _logger.LogWarning("Rejecting invalid path '{Path}': {Reason}", rawPath, validation.FailureReason);
            return validation;
        }

        // Check for duplicate root in database
        var existing = await _database.GetRootByNormalizedPathAsync(validation.NormalizedPath, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Root already exists: {Path}", validation.CanonicalPath);
            return PathValidationResult.Failure($"The path '{validation.CanonicalPath}' is already configured as an indexing root.");
        }

        var newRoot = new SearchRoot
        {
            Id = SearchRootId.New(),
            Path = validation.CanonicalPath,
            NormalizedPath = validation.NormalizedPath,
            RootType = validation.RootType,
            IsEnabled = true,
            Status = RootStatus.Idle,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _database.AddRootAsync(newRoot, cancellationToken);
        _logger.LogInformation("Added new search root: {Path} ({Type})", newRoot.Path, newRoot.RootType);

        return validation;
    }

    public async Task<IReadOnlyList<SearchRoot>> GetAllRootsAsync(CancellationToken cancellationToken = default)
    {
        return await _database.GetAllRootsAsync(cancellationToken);
    }

    public async Task RemoveRootAsync(SearchRootId id, CancellationToken cancellationToken = default)
    {
        var root = await _database.GetRootByIdAsync(id, cancellationToken);
        if (root != null)
        {
            await _database.DeleteRootAsync(id, cancellationToken);
            _logger.LogInformation("Removed search root '{Path}' (Id={Id}). Physical files were NOT modified or deleted.", root.Path, id);
        }
    }

    public async Task ToggleRootAsync(SearchRootId id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var root = await _database.GetRootByIdAsync(id, cancellationToken);
        if (root != null)
        {
            root.IsEnabled = isEnabled;
            await _database.UpdateRootAsync(root, cancellationToken);
            _logger.LogInformation("Updated root '{Path}' IsEnabled={Enabled}", root.Path, isEnabled);
        }
    }
}

