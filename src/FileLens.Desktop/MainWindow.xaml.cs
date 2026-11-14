using System.Windows;
using Microsoft.Extensions.Logging;
using FileLens.Desktop.ViewModels;
using FileLens.Domain.Interfaces;

namespace FileLens.Desktop;

/// <summary>
/// Main application window with Root Management and Indexing Dashboard.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAiWorkerHealthCheck _healthCheck;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        MainViewModel viewModel,
        IAiWorkerHealthCheck healthCheck,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _healthCheck = healthCheck;
        _logger = logger;

        InitializeComponent();
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Initialize data and roots from database
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
        }

        // 2. Perform AI Worker health check (Phase 0 integration)
        try
        {
            var result = await _healthCheck.CheckHealthAsync();
            var statusIcon = result.IsHealthy ? "✓" : "⚠";
            AiWorkerStatusText.Text = $"{statusIcon} AI Worker: {result.Message} ({result.WorkerType})";
            _logger.LogInformation(
                "AI Worker health check: Healthy={IsHealthy}, Worker={WorkerType}, Message={Message}",
                result.IsHealthy, result.WorkerType, result.Message);
        }
        catch (Exception ex)
        {
            AiWorkerStatusText.Text = "✗ AI Worker: Health check failed";
            _logger.LogError(ex, "Health check failed");
        }
    }
}
