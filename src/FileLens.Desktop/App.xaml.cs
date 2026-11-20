using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using FileLens.Application.Configuration;
using FileLens.Application.Services;
using FileLens.Domain.Interfaces;
using FileLens.Infrastructure;
using FileLens.Desktop.ViewModels;

namespace FileLens.Desktop;

/// <summary>
/// Application entry point. Configures DI, logging, options binding and starts the main window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Configure Serilog
        var rawLogDir = configuration.GetSection("FileLens:LogDirectory").Value;
        var logDirectory = !string.IsNullOrWhiteSpace(rawLogDir)
            ? Environment.ExpandEnvironmentVariables(rawLogDir)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FileLens", "logs");

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch (Exception)
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "FileLens", "logs");
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "filelens-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("FileLens starting up");

        var services = new ServiceCollection();

        // Configuration
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<FileLensOptions>(options =>
        {
            var section = configuration.GetSection(FileLensOptions.SectionName);
            var cacheDir = section["CacheDirectory"];
            var logDir = section["LogDirectory"];
            options.CacheDirectory = !string.IsNullOrWhiteSpace(cacheDir)
                ? Environment.ExpandEnvironmentVariables(cacheDir)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileLens", "cache");
            options.LogDirectory = !string.IsNullOrWhiteSpace(logDir)
                ? Environment.ExpandEnvironmentVariables(logDir)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileLens", "logs");

            if (int.TryParse(section["MaxConcurrency"], out var maxConcurrency))
            {
                options.MaxConcurrency = maxConcurrency;
            }
            if (long.TryParse(section["MaxFileSizeBytes"], out var maxFileSizeBytes))
            {
                options.MaxFileSizeBytes = maxFileSizeBytes;
            }
        });
        services.AddSingleton<IValidateOptions<FileLensOptions>, FileLensOptionsValidator>();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // Infrastructure
        services.AddInfrastructure();

        // Domain & Application services
        services.AddSingleton<RootManagementService>();
        services.AddSingleton<IndexingJobEngine>();
        services.AddSingleton<IIndexingJobEngine>(sp => sp.GetRequiredService<IndexingJobEngine>());
        services.AddSingleton<DocumentExtractionEngine>();
        services.AddSingleton<IDocumentExtractionEngine>(sp => sp.GetRequiredService<DocumentExtractionEngine>());
        services.AddSingleton<IOcrJobEngine, OcrJobEngine>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<VisualSearchViewModel>();

        // UI & ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // Validate options eagerly at startup
        try
        {
            var optionsMonitor = _serviceProvider.GetRequiredService<IOptionsMonitor<FileLensOptions>>();
            _ = optionsMonitor.CurrentValue; // triggers validation
            Log.Information("Configuration validated successfully");
        }
        catch (OptionsValidationException ex)
        {
            Log.Error(ex, "Configuration validation failed");
            MessageBox.Show(
                $"Configuration error:\n{ex.Message}",
                "FileLens — Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Initialize SQLite Database and recover interrupted jobs
        try
        {
            var database = _serviceProvider.GetRequiredService<IIndexDatabase>();
            database.InitializeAsync().GetAwaiter().GetResult();
            database.RecoverInterruptedJobsAsync().GetAwaiter().GetResult();
            Log.Information("Database initialized and checked for recovery successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database initialization failed");
            MessageBox.Show(
                $"Database initialization error:\n{ex.Message}",
                "FileLens — Database Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        Log.Information("FileLens shutting down");
        Log.CloseAndFlush();
        _serviceProvider?.Dispose();
    }
}

