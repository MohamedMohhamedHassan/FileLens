using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly IDocumentExtractionEngine? _extractionEngine;
    private readonly ISearchService? _searchService;
    private readonly Dispatcher _dispatcher;

    // Phase 1 - Roots & Discovery
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

    // Phase 2 - Extraction Stats & Controls
    private int _filesAwaitingExtraction;
    private int _successfullyExtracted;
    private int _failedExtractions;
    private int _unsupportedFormats;
    private int _documentsUpdated;
    private string _indexedTextSizeDisplay = "0 B";
    private bool _isExtracting;
    private string _extractionStatusText = "Idle";
    private double _extractionProgressPercent;

    // Phase 3 - OCR Controls
    private readonly IOcrJobEngine? _ocrJobEngine;
    private string _ocrStatusText = "OCR: Ready";
    private bool _isOcrRunning;
    private double _ocrProgressPercent;
    private CancellationTokenSource? _ocrCts;

    // Phase 2 - Search & Preview
    private string _searchText = string.Empty;
    private string _selectedExtensionFilter = "All Formats";
    private string _selectedDateFilter = "Any Time";
    private SearchResultViewModel? _selectedSearchResult;
    private string _previewTitle = "No Document Selected";
    private string _previewLocation = string.Empty;
    private string _previewContent = "Select a search result to view its extracted text and structural locator (page, sheet, slide, or paragraph).";
    private string _previewMetadata = string.Empty;
    private bool _isSearching;
    private string _searchStatsSummary = "Ready to search across indexed documents.";

    public ObservableCollection<SearchRootViewModel> Roots { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    public IReadOnlyList<string> ExtensionFilters { get; } = new[]
    {
        "All Formats",
        "PDF Documents (*.pdf)",
        "Word Documents (*.docx)",
        "Excel Spreadsheets (*.xlsx)",
        "PowerPoint Slides (*.pptx)",
        "Plain Text & Data (*.txt, *.csv)",
        "Source Code (*.cs, *.py, *.js...)",
        "Structured Data (*.json, *.xml)",
        "Images & OCR (*.jpg, *.png, *.pdf)"
    };

    public IReadOnlyList<string> DateFilters { get; } = new[]
    {
        "Any Time",
        "Past 24 Hours",
        "Past 7 Days",
        "Past 30 Days",
        "Past Year"
    };

    // Properties - Roots
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

    // Properties - Extraction
    public int FilesAwaitingExtraction
    {
        get => _filesAwaitingExtraction;
        set { _filesAwaitingExtraction = value; OnPropertyChanged(); }
    }

    public int SuccessfullyExtracted
    {
        get => _successfullyExtracted;
        set { _successfullyExtracted = value; OnPropertyChanged(); }
    }

    public int FailedExtractions
    {
        get => _failedExtractions;
        set { _failedExtractions = value; OnPropertyChanged(); }
    }

    public int UnsupportedFormats
    {
        get => _unsupportedFormats;
        set { _unsupportedFormats = value; OnPropertyChanged(); }
    }

    public int DocumentsUpdated
    {
        get => _documentsUpdated;
        set { _documentsUpdated = value; OnPropertyChanged(); }
    }

    public string IndexedTextSizeDisplay
    {
        get => _indexedTextSizeDisplay;
        set { _indexedTextSizeDisplay = value; OnPropertyChanged(); }
    }

    public bool IsExtracting
    {
        get => _isExtracting;
        set { _isExtracting = value; OnPropertyChanged(); }
    }

    public string ExtractionStatusText
    {
        get => _extractionStatusText;
        set { _extractionStatusText = value; OnPropertyChanged(); }
    }

    public double ExtractionProgressPercent
    {
        get => _extractionProgressPercent;
        set { _extractionProgressPercent = value; OnPropertyChanged(); }
    }

    public string OcrStatusText
    {
        get => _ocrStatusText;
        set { _ocrStatusText = value; OnPropertyChanged(); }
    }

    public bool IsOcrRunning
    {
        get => _isOcrRunning;
        set { _isOcrRunning = value; OnPropertyChanged(); }
    }

    public double OcrProgressPercent
    {
        get => _ocrProgressPercent;
        set { _ocrProgressPercent = value; OnPropertyChanged(); }
    }

    // Properties - Search & Preview
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    public string SelectedExtensionFilter
    {
        get => _selectedExtensionFilter;
        set { _selectedExtensionFilter = value; OnPropertyChanged(); }
    }

    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set { _selectedDateFilter = value; OnPropertyChanged(); }
    }

    public SearchResultViewModel? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            _selectedSearchResult = value;
            OnPropertyChanged();
            LoadPreviewForSelectedResult();
        }
    }

    public string PreviewTitle
    {
        get => _previewTitle;
        set { _previewTitle = value; OnPropertyChanged(); }
    }

    public string PreviewLocation
    {
        get => _previewLocation;
        set { _previewLocation = value; OnPropertyChanged(); }
    }

    public string PreviewContent
    {
        get => _previewContent;
        set { _previewContent = value; OnPropertyChanged(); }
    }

    public string PreviewMetadata
    {
        get => _previewMetadata;
        set { _previewMetadata = value; OnPropertyChanged(); }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set { _isSearching = value; OnPropertyChanged(); }
    }

    public string SearchStatsSummary
    {
        get => _searchStatsSummary;
        set { _searchStatsSummary = value; OnPropertyChanged(); }
    }

    public bool IsRunning => _jobEngine.IsRunning;
    public bool IsPaused => _jobEngine.IsPaused;

    // Commands - Roots & Discovery
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

    // Commands - Extraction & Search
    public ICommand StartExtractionCommand { get; }
    public ICommand CancelExtractionCommand { get; }
    public ICommand StartOcrCommand { get; }
    public ICommand CancelOcrCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand OpenSelectedFileCommand { get; }
    public ICommand OpenSelectedFolderCommand { get; }

    public MainViewModel(
        RootManagementService rootService,
        IIndexingJobEngine jobEngine,
        IIndexDatabase database)
        : this(rootService, jobEngine, database, null!, null!, null)
    {
    }

    public MainViewModel(
        RootManagementService rootService,
        IIndexingJobEngine jobEngine,
        IIndexDatabase database,
        IDocumentExtractionEngine? extractionEngine,
        ISearchService? searchService)
        : this(rootService, jobEngine, database, extractionEngine, searchService, null)
    {
    }

    public MainViewModel(
        RootManagementService rootService,
        IIndexingJobEngine jobEngine,
        IIndexDatabase database,
        IDocumentExtractionEngine? extractionEngine,
        ISearchService? searchService,
        IOcrJobEngine? ocrJobEngine)
    {
        _rootService = rootService;
        _jobEngine = jobEngine;
        _database = database;
        _extractionEngine = extractionEngine!;
        _searchService = searchService!;
        _ocrJobEngine = ocrJobEngine;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _jobEngine.ProgressChanged += OnProgressChanged;
        _jobEngine.ErrorOccurred += OnErrorOccurred;
        _jobEngine.LogMessage += OnLogMessage;

        if (_extractionEngine != null)
        {
            _extractionEngine.ProgressChanged += OnExtractionProgress;
            _extractionEngine.LogMessage += OnLogMessage;
        }

        if (_ocrJobEngine != null)
        {
            _ocrJobEngine.ProgressChanged += OnOcrProgress;
            _ocrJobEngine.LogMessage += OnLogMessage;
        }

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

        StartExtractionCommand = new RelayCommand(async () => await ExecuteStartExtractionAsync(), () => !IsExtracting && _extractionEngine != null);
        CancelExtractionCommand = new RelayCommand(() => _extractionEngine?.Cancel(), () => IsExtracting && _extractionEngine != null);
        StartOcrCommand = new RelayCommand(async () => await ExecuteStartOcrAsync(), () => !IsOcrRunning && _ocrJobEngine != null);
        CancelOcrCommand = new RelayCommand(ExecuteCancelOcr, () => IsOcrRunning && _ocrJobEngine != null);
        SearchCommand = new RelayCommand(async () => await ExecuteSearchAsync(), () => _searchService != null);
        ClearSearchCommand = new RelayCommand(ExecuteClearSearch);

        OpenSelectedFileCommand = new RelayCommand(() => SelectedSearchResult?.OpenFileCommand.Execute(null), () => SelectedSearchResult != null);
        OpenSelectedFolderCommand = new RelayCommand(() => SelectedSearchResult?.OpenFolderCommand.Execute(null), () => SelectedSearchResult != null);
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

    private async Task ExecuteStartExtractionAsync()
    {
        IsExtracting = true;
        ExtractionStatusText = "Extracting text & chunks...";
        CommandManager.InvalidateRequerySuggested();

        try
        {
            if (_extractionEngine != null)
            {
                await _extractionEngine.RunExtractionAsync();
            }
        }
        finally
        {
            IsExtracting = false;
            ExtractionStatusText = "Idle";
            await RefreshStatisticsAsync();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ExecuteStartOcrAsync()
    {
        if (_ocrJobEngine == null) return;
        IsOcrRunning = true;
        OcrStatusText = "OCR: Starting...";
        OcrProgressPercent = 0;
        _ocrCts = new CancellationTokenSource();
        CommandManager.InvalidateRequerySuggested();

        try
        {
            await _ocrJobEngine.RunOcrPipelineAsync(_ocrCts.Token);
            OcrStatusText = $"OCR: Completed ({_ocrJobEngine.ProcessedCount} processed, {_ocrJobEngine.FailedCount} failed)";
            await RefreshStatisticsAsync();
        }
        catch (OperationCanceledException)
        {
            OcrStatusText = "OCR: Cancelled by user";
        }
        catch (Exception ex)
        {
            OcrStatusText = $"OCR Error: {ex.Message}";
        }
        finally
        {
            IsOcrRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ExecuteCancelOcr()
    {
        _ocrCts?.Cancel();
        _ocrJobEngine?.Cancel();
        OcrStatusText = "OCR: Cancelling...";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchStatsSummary = "Enter search terms to find matching files.";
            return;
        }

        IsSearching = true;
        SearchStatsSummary = "Searching...";

        try
        {
            var extList = MapFilterToExtensions(SelectedExtensionFilter);
            var (fromDate, toDate) = MapDateFilter(SelectedDateFilter);

            var query = new SearchQuery
            {
                Text = SearchText.Trim(),
                Extensions = extList,
                FromDateUtc = fromDate,
                ToDateUtc = toDate,
                PageNumber = 1,
                PageSize = 100
            };

            if (_searchService == null) return;
            var page = await _searchService.SearchAsync(query);

            _dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var item in page.Items)
                {
                    SearchResults.Add(new SearchResultViewModel(item));
                }

                SearchStatsSummary = $"Found {page.TotalMatches} matches in {page.SearchDurationMs} ms for '{page.QueryUsed}'";
                if (SearchResults.Count > 0)
                {
                    SelectedSearchResult = SearchResults[0];
                }
                else
                {
                    PreviewTitle = "No Results Found";
                    PreviewLocation = string.Empty;
                    PreviewContent = $"No documents matched '{SearchText}'. Try different search terms or verify document extraction is completed in the Indexing tab.";
                    PreviewMetadata = string.Empty;
                }
            });
        }
        catch (Exception ex)
        {
            SearchStatsSummary = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private void ExecuteClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        SelectedSearchResult = null;
        PreviewTitle = "No Document Selected";
        PreviewLocation = string.Empty;
        PreviewContent = "Select a search result to view its extracted text and structural locator.";
        PreviewMetadata = string.Empty;
        SearchStatsSummary = "Ready to search across indexed documents.";
    }

    private async void LoadPreviewForSelectedResult()
    {
        if (SelectedSearchResult == null) return;

        var result = SelectedSearchResult;
        PreviewTitle = result.FileName;
        PreviewLocation = result.LocationBadge;

        try
        {
            if (_searchService == null) return;
            var extractedDoc = await _searchService.GetDocumentPreviewAsync(result.FileId);
            _dispatcher.Invoke(() =>
            {
                if (extractedDoc != null && !string.IsNullOrWhiteSpace(extractedDoc.RawText))
                {
                    // If matched a specific chunk locator, show chunk text first or locate it
                    if (result.PageNumber.HasValue || !string.IsNullOrEmpty(result.SheetName) || result.SlideNumber.HasValue)
                    {
                        var matchingChunk = extractedDoc.Chunks.FirstOrDefault(c =>
                            (result.PageNumber.HasValue && c.PageNumber == result.PageNumber) ||
                            (!string.IsNullOrEmpty(result.SheetName) && c.SheetName == result.SheetName) ||
                            (result.SlideNumber.HasValue && c.SlideNumber == result.SlideNumber));

                        if (matchingChunk != null)
                        {
                            PreviewContent = $"=== MATCHED LOCATOR: {matchingChunk.LocationReference} ===\n\n{matchingChunk.RawText}\n\n=========================================\n\nFull Document Text:\n\n{extractedDoc.RawText}";
                        }
                        else
                        {
                            PreviewContent = extractedDoc.RawText;
                        }
                    }
                    else
                    {
                        PreviewContent = extractedDoc.RawText;
                    }

                    var metaList = extractedDoc.Metadata.Select(kv => $"{kv.Key}: {kv.Value}").ToList();
                    metaList.Insert(0, $"Path: {result.FullPath}");
                    metaList.Insert(1, $"Size: {result.SizeDisplay}");
                    metaList.Insert(2, $"Modified: {result.ModifiedDisplay}");
                    PreviewMetadata = string.Join("  •  ", metaList);
                }
                else
                {
                    PreviewContent = !string.IsNullOrWhiteSpace(result.Snippet)
                        ? $"Snippet:\n{result.CleanSnippet}\n\n(Full document text is awaiting extraction or format is unsupported)"
                        : "No extracted text available for this document.";
                    PreviewMetadata = $"Path: {result.FullPath}  •  Size: {result.SizeDisplay}";
                }
            });
        }
        catch (Exception ex)
        {
            PreviewContent = $"Error loading preview: {ex.Message}";
        }
    }

    private static IReadOnlyList<string>? MapFilterToExtensions(string filter)
    {
        return filter switch
        {
            "PDF Documents (*.pdf)" => new[] { ".pdf" },
            "Word Documents (*.docx)" => new[] { ".docx", ".docm", ".dotx" },
            "Excel Spreadsheets (*.xlsx)" => new[] { ".xlsx", ".xlsm", ".xltx" },
            "PowerPoint Slides (*.pptx)" => new[] { ".pptx", ".pptm", ".potx" },
            "Plain Text & Data (*.txt, *.csv)" => new[] { ".txt", ".csv", ".tsv", ".log", ".ini" },
            "Source Code (*.cs, *.py, *.js...)" => new[] { ".cs", ".py", ".js", ".ts", ".jsx", ".tsx", ".cpp", ".c", ".h", ".java", ".go", ".rs", ".sql", ".sh", ".ps1", ".md", ".yaml", ".yml" },
            "Structured Data (*.json, *.xml)" => new[] { ".json", ".jsonl", ".xml", ".xaml", ".svg" },
            "Images & OCR (*.jpg, *.png, *.pdf)" => new[] { ".jpg", ".jpeg", ".png", ".tiff", ".bmp", ".webp", ".gif", ".pdf" },
            _ => null
        };
    }

    private static (DateTime? fromDate, DateTime? toDate) MapDateFilter(string filter)
    {
        var now = DateTime.UtcNow;
        return filter switch
        {
            "Past 24 Hours" => (now.AddDays(-1), null),
            "Past 7 Days" => (now.AddDays(-7), null),
            "Past 30 Days" => (now.AddDays(-30), null),
            "Past Year" => (now.AddDays(-365), null),
            _ => (null, null)
        };
    }

    private async Task RefreshStatisticsAsync()
    {
        var stats = await _database.GetStatisticsAsync();
        var extStats = await _database.GetExtractionStatisticsAsync();

        _dispatcher.Invoke(() =>
        {
            TotalRoots = stats.TotalRoots;
            TotalFilesIndexed = stats.TotalFilesIndexed;
            TotalSizeDisplay = FormatBytes(stats.TotalSizeBytes);
            TotalErrors = stats.TotalErrors;

            FilesAwaitingExtraction = extStats.AwaitingExtraction;
            SuccessfullyExtracted = extStats.SuccessfullyExtracted;
            FailedExtractions = extStats.FailedExtractions;
            UnsupportedFormats = extStats.UnsupportedFormats;
            DocumentsUpdated = extStats.DocumentsUpdated;
            IndexedTextSizeDisplay = FormatBytes(extStats.IndexedTextSizeBytes);
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

            var matchingRoot = Roots.FirstOrDefault(r => r.Id == progress.RootId);
            matchingRoot?.Refresh();

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            CommandManager.InvalidateRequerySuggested();
        });
    }

    private void OnExtractionProgress(object? sender, ExtractionProgress progress)
    {
        _dispatcher.Invoke(() =>
        {
            if (progress.TotalFiles > 0)
            {
                ExtractionProgressPercent = Math.Min(100.0, (double)progress.ProcessedFiles / progress.TotalFiles * 100.0);
            }

            ExtractionStatusText = !string.IsNullOrEmpty(progress.CurrentFile)
                ? $"Extracting: {Path.GetFileName(progress.CurrentFile)}"
                : "Completed";

            FilesAwaitingExtraction = Math.Max(0, progress.TotalFiles - progress.ProcessedFiles);
            SuccessfullyExtracted = progress.ExtractedCount;
            FailedExtractions = progress.FailedCount;
            UnsupportedFormats = progress.SkippedCount;

            CommandManager.InvalidateRequerySuggested();
        });
    }

    private void OnOcrProgress(object? sender, OcrJobProgress progress)
    {
        _dispatcher.Invoke(() =>
        {
            if (progress.TotalFiles > 0)
            {
                OcrProgressPercent = Math.Min(100.0, (double)progress.ProcessedFiles / progress.TotalFiles * 100.0);
                OcrStatusText = !string.IsNullOrEmpty(progress.CurrentFile)
                    ? $"OCR: {progress.ProcessedFiles}/{progress.TotalFiles} ({OcrProgressPercent:F0}%) — {Path.GetFileName(progress.CurrentFile)}"
                    : $"OCR: {progress.ProcessedFiles}/{progress.TotalFiles} Completed";
            }
            else
            {
                OcrProgressPercent = 0;
                OcrStatusText = "OCR: No files queued";
            }
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
            OnPropertyChanged(nameof(IsExtracting));
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
