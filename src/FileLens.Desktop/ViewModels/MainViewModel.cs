using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FileLens.Application.Services;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;

namespace FileLens.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RootManagementService _rootService;
    private readonly IIndexingJobEngine _jobEngine;
    private readonly IIndexDatabase _database;
    private readonly Dispatcher _dispatcher;

    private SearchRootViewModel? _selectedRoot;
    private string _newLocationPath = string.Empty;
    private string _currentDirectory = "-";
    private string _currentFile = "-";
    private int _discoveredCount;
    private int _processedCount;
    private int _skippedCount;
    private int _failedCount;
    private double _progressPercent;
    private bool _isIndeterminate = false;
    private string _jobStatusText = "Idle";

    private int _totalRoots;
    private int _totalFilesIndexed;
    private string _totalSizeDisplay = "0 B";
    private int _totalErrors;

    public ObservableCollection<SearchRootViewModel> Roots { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();

    public SearchRootViewModel? SelectedRoot
    {
        get => _selectedRoot;
        set { _selectedRoot = value; OnPropertyChanged(); }
    }

    public string NewLocationPath
    {
        get => _newLocationPath;
        set { _newLocationPath = value; OnPropertyChanged(); }
    }

    public string CurrentDirectory
    {
        get => _currentDirectory;
        set { _currentDirectory = value; OnPropertyChanged(); }
    }

    public string CurrentFile
    {
        get => _currentFile;
        set { _currentFile = value; OnPropertyChanged(); }
    }

    public int DiscoveredCount
    {
        get => _discoveredCount;
        set { _discoveredCount = value; OnPropertyChanged(); }
    }

    public int ProcessedCount
    {
        get => _processedCount;
        set { _processedCount = value; OnPropertyChanged(); }
    }

    public int SkippedCount
    {
        get => _skippedCount;
        set { _skippedCount = value; OnPropertyChanged(); }
    }

    public int FailedCount
    {
        get => _failedCount;
        set { _failedCount = value; OnPropertyChanged(); }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set { _isIndeterminate = value; OnPropertyChanged(); }
    }

    public string JobStatusText
    {
        get => _jobStatusText;
        set { _jobStatusText = value; OnPropertyChanged(); }
    }

    public int TotalRoots
    {
        get => _totalRoots;
        set { _totalRoots = value; OnPropertyChanged(); }
    }

    public int TotalFilesIndexed
    {
        get => _totalFilesIndexed;
        set { _totalFilesIndexed = value; OnPropertyChanged(); }
    }

    public string TotalSizeDisplay
    {
        get => _totalSizeDisplay;
        set { _totalSizeDisplay = value; OnPropertyChanged(); }
    }

    public int TotalErrors
    {
        get => _totalErrors;
        set { _totalErrors = value; OnPropertyChanged(); }
    }

    public bool IsRunning => _jobEngine.IsRunning;
    public bool IsPaused => _jobEngine.IsPaused;

    // Commands
    public ICommand BrowseFolderCommand { get; }
    public ICommand AddLocationCommand { get; }
    public ICommand RemoveLocationCommand { get; }
    public ICommand ToggleLocationCommand { get; }
    public ICommand StartIndexingCommand { get; }
    public ICommand PauseIndexingCommand { get; }
    public ICommand ResumeIndexingCommand { get; }
    public ICommand CancelIndexingCommand { get; }
    public ICommand RetryFailedCommand { get; }
    public ICommand RefreshCommand { get; }

    public MainViewModel(
        RootManagementService rootService,
        IIndexingJobEngine jobEngine,
        IIndexDatabase database)
    {
        _rootService = rootService;
        _jobEngine = jobEngine;
        _database = database;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _jobEngine.ProgressChanged += OnProgressChanged;
        _jobEngine.ErrorOccurred += OnErrorOccurred;
        _jobEngine.LogMessage += OnLogMessage;

        BrowseFolderCommand = new RelayCommand(ExecuteBrowseFolder);
        AddLocationCommand = new RelayCommand(async () => await ExecuteAddLocationAsync());
        RemoveLocationCommand = new RelayCommand(async () => await ExecuteRemoveLocationAsync(), () => SelectedRoot != null);
        ToggleLocationCommand = new RelayCommand(async (param) => await ExecuteToggleLocationAsync(param as SearchRootViewModel));
        StartIndexingCommand = new RelayCommand(async () => await ExecuteStartIndexingAsync());
        PauseIndexingCommand = new RelayCommand(() => _jobEngine.PauseIndexing(), () => IsRunning && !IsPaused);
        ResumeIndexingCommand = new RelayCommand(() => _jobEngine.ResumeIndexing(), () => IsRunning && IsPaused);
        CancelIndexingCommand = new RelayCommand(() => _jobEngine.CancelIndexing(), () => IsRunning);
        RetryFailedCommand = new RelayCommand(async () => await _jobEngine.RetryFailedJobsAsync());
        RefreshCommand = new RelayCommand(async () => await LoadDataAsync());
    }

    public async Task InitializeAsync()
    {
        await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        var roots = await _rootService.GetAllRootsAsync();
        _dispatcher.Invoke(() =>
        {
            Roots.Clear();
            foreach (var r in roots)
            {
                Roots.Add(new SearchRootViewModel(r));
            }
        });

        await RefreshStatisticsAsync();
    }

    private void ExecuteBrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder to Index with FileLens",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            NewLocationPath = dialog.FolderName;
        }
    }

    private async Task ExecuteAddLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(NewLocationPath))
        {
            MessageBox.Show("Please enter or browse to a valid folder or UNC network share.", "FileLens", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = await _rootService.ValidateAndAddRootAsync(NewLocationPath);
        if (!result.IsValid)
        {
            MessageBox.Show(result.FailureReason, "Path Validation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        NewLocationPath = string.Empty;
        await LoadDataAsync();
    }

    private async Task ExecuteRemoveLocationAsync()
    {
        if (SelectedRoot == null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to remove '{SelectedRoot.Path}' from FileLens?\n\nNOTE: Physical files on disk will NOT be touched.",
            "Confirm Remove Location",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            await _rootService.RemoveRootAsync(SelectedRoot.Id);
            await LoadDataAsync();
        }
    }

    private async Task ExecuteToggleLocationAsync(SearchRootViewModel? rootVm)
    {
        if (rootVm == null) return;
        await _rootService.ToggleRootAsync(rootVm.Id, rootVm.IsEnabled);
        await RefreshStatisticsAsync();
    }

    private async Task ExecuteStartIndexingAsync()
    {
        if (SelectedRoot != null && SelectedRoot.IsEnabled)
        {
            await _jobEngine.StartIndexingAsync(SelectedRoot.Id);
        }
        else
        {
            await _jobEngine.StartIndexingAllAsync();
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RefreshStatisticsAsync()
    {
        var stats = await _database.GetStatisticsAsync();
        _dispatcher.Invoke(() =>
        {
            TotalRoots = stats.TotalRoots;
            TotalFilesIndexed = stats.TotalFilesIndexed;
            TotalSizeDisplay = FormatBytes(stats.TotalSizeBytes);
            TotalErrors = stats.TotalErrors;
        });
    }

    private void OnProgressChanged(object? sender, JobProgress progress)
    {
        _dispatcher.Invoke(() =>
        {
            CurrentDirectory = progress.CurrentDirectory ?? "-";
            CurrentFile = progress.CurrentFile ?? "-";
            DiscoveredCount = progress.DiscoveredCount;
            ProcessedCount = progress.ProcessedCount;
            SkippedCount = progress.SkippedCount;
            FailedCount = progress.FailedCount;

            if (progress.DiscoveredCount > 0)
            {
                IsIndeterminate = false;
                ProgressPercent = Math.Min(100.0, (double)progress.ProcessedCount / progress.DiscoveredCount * 100.0);
            }
            else
            {
                IsIndeterminate = true;
            }

            JobStatusText = _jobEngine.IsPaused ? "Paused" : (_jobEngine.IsRunning ? "Indexing..." : "Completed");

            // Update matching root VM
            var matchingRoot = Roots.FirstOrDefault(r => r.Id == progress.RootId);
            matchingRoot?.Refresh();

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private void OnErrorOccurred(object? sender, IndexErrorRecord error)
    {
        _dispatcher.Invoke(() =>
        {
            TotalErrors++;
            LogMessages.Insert(0, $"[{error.LoggedAtUtc:HH:mm:ss}] [ERROR] [{error.ErrorCategory}] {error.FilePath}: {error.Message}");
        });
    }

    private void OnLogMessage(object? sender, string message)
    {
        _dispatcher.Invoke(() =>
        {
            LogMessages.Insert(0, message);
            if (LogMessages.Count > 200)
            {
                LogMessages.RemoveAt(LogMessages.Count - 1);
            }
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

