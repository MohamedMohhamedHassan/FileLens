using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using FileLens.Application.Configuration;

namespace FileLens.Infrastructure.Database;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IOptions<FileLensOptions> options)
    {
        string baseDir = options.Value.CacheDirectory;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileLens", "cache");
        }

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch (Exception)
        {
            baseDir = Path.Combine(Path.GetTempPath(), "FileLens", "cache");
            Directory.CreateDirectory(baseDir);
        }

        string dbPath = Path.Combine(baseDir, "filelens_index.db");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10
        };

        _connectionString = builder.ToString();
    }

    public SqliteConnectionFactory(string customDbPath)
    {
        var dir = Path.GetDirectoryName(customDbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = customDbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10
        };

        _connectionString = builder.ToString();
    }

    public async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Apply robust WAL mode, foreign keys, and busy timeout
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}

