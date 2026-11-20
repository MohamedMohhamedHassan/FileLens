using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Win32;
using FileLens.Domain.Interfaces;
using FileLens.Domain.Models;
using FileLens.Infrastructure.Visual;

namespace FileLens.Desktop.ViewModels;

public sealed class VisualSearchViewModel : INotifyPropertyChanged
{
    private readonly IImageEmbeddingProvider _embeddingProvider;
    private readonly IVectorIndex _vectorIndex;
    private readonly VisualIndexingService _indexingService;
    private readonly IIndexDatabase _database;
    private string _referencePath = string.Empty;
    private string _status = "Visual search is local-only and ready.";
    private double _threshold = 0.35;
    private int _limit = 25;
    private RegionSelection? _selection;
    private bool _isBusy;

    public VisualSearchViewModel(IImageEmbeddingProvider embeddingProvider, IVectorIndex vectorIndex, VisualIndexingService indexingService, IIndexDatabase database)
    {
        _embeddingProvider = embeddingProvider; _vectorIndex = vectorIndex; _indexingService = indexingService; _database = database;
        UploadReferenceCommand = new RelayCommand(ChooseReference);
        SearchCommand = new RelayCommand(async () => await SearchAsync(), () => !IsBusy && File.Exists(ReferencePath));
        ClearSelectionCommand = new RelayCommand(() => { Selection = null; Status = "Region cleared; the next search uses the full image."; });
        ClearReferenceCommand = new RelayCommand(() => { ReferencePath = string.Empty; Results.Clear(); Status = "Reference cleared."; });
        IndexImagesCommand = new RelayCommand(async () => await IndexImagesAsync(), () => !IsBusy);
    }

    public ObservableCollection<VisualSearchResultViewModel> Results { get; } = new();
    public ICommand UploadReferenceCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ClearReferenceCommand { get; }
    public ICommand IndexImagesCommand { get; }
    public string ReferencePath { get => _referencePath; set { _referencePath = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public double Threshold { get => _threshold; set { _threshold = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
    public int Limit { get => _limit; set { _limit = value; OnPropertyChanged(); } }
    public IReadOnlyList<int> Limits { get; } = new[] { 10, 25, 50, 100 };
    public RegionSelection? Selection { get => _selection; set { _selection = value?.Clamp(); OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

    private void ChooseReference(object? _)
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.tiff;*.bmp;*.webp;*.gif" };
        if (dialog.ShowDialog() == true) ReferencePath = dialog.FileName;
    }

    private async Task SearchAsync()
    {
        IsBusy = true; Results.Clear(); Status = "Creating local visual descriptor and searching...";
        try
        {
            var descriptor = await _embeddingProvider.CreateEmbeddingAsync(await File.ReadAllBytesAsync(ReferencePath));
            var results = await _vectorIndex.SearchAsync(descriptor.Embedding.Vector, Limit, Threshold, descriptor.Embedding.ProviderName, descriptor.Embedding.ModelName, descriptor.Embedding.ModelVersion);
            foreach (var result in results) Results.Add(new VisualSearchResultViewModel(result));
            Status = $"{Results.Count} {descriptor.Embedding.ProviderName} semantic matches. Score is cosine similarity, not probability.";
        }
        catch (Exception ex) { Status = $"Visual search failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task IndexImagesAsync()
    {
        IsBusy = true; Status = "Indexing approved image files locally...";
        try
        {
            var files = new List<IndexedFile>();
            foreach (var root in await _database.GetAllRootsAsync()) files.AddRange(await _database.GetFilesByRootAsync(root.Id, 100000));
            await _indexingService.IndexFilesAsync(files);
            Status = "Visual image indexing completed.";
        }
        catch (Exception ex) { Status = $"Visual indexing failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VisualSearchResultViewModel
{
    private readonly VisualSearchResult _result;
    public VisualSearchResultViewModel(VisualSearchResult result) => _result = result;
    public string FileName => _result.FileName;
    public string FullPath => _result.FullPath;
    public string MatchType => _result.MatchType;
    public string Similarity => $"{_result.Similarity:P1}";
    public string Dimensions => $"{_result.Width} × {_result.Height}";
    public ICommand OpenFileCommand => new RelayCommand(() => Process.Start(new ProcessStartInfo(_result.FullPath) { UseShellExecute = true }));
    public ICommand OpenFolderCommand => new RelayCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_result.FullPath}\"") { UseShellExecute = true }));
}
