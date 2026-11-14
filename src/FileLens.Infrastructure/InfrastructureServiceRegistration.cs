using Microsoft.Extensions.DependencyInjection;
using FileLens.Domain.Interfaces;
using FileLens.Infrastructure.Database;
using FileLens.Infrastructure.FileSystem;

namespace FileLens.Infrastructure;

/// <summary>
/// Registers infrastructure services with the dependency injection container.
/// </summary>
public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Adds SQLite database and filesystem infrastructure services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<IFileDiscoveryService, SafeFileDiscoveryService>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IIndexDatabase, SqliteDatabase>();

        return services;
    }
}
