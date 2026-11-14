using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FileLens.Domain.Models;

namespace FileLens.Desktop.ViewModels;

public sealed class SearchResultViewModel
{
    private readonly SearchResult _model;

    public SearchResult Model => _model;

    public Guid FileId => _model.FileId;
    public string FileName => _model.FileName;
    public string FullPath => _model.FullPath;
    public string Extension => _model.Extension;
    public long SizeBytes => _model.SizeBytes;
    public DateTime ModifiedAtUtc => _model.ModifiedAtUtc;
    public double Score => _model.Score;
    public string Snippet => _model.Snippet;
    public string? LocationReference => _model.LocationReference;
    public int? PageNumber => _model.PageNumber;
    public string? SheetName => _model.SheetName;
    public string? CellAddress => _model.CellAddress;
    public int? SlideNumber => _model.SlideNumber;
    public bool IsOcrMatch => _model.IsOcrMatch;
    public double? OcrConfidence => _model.OcrConfidence;
    public string? OcrLanguage => _model.OcrLanguage;

    public string SizeDisplay => FormatBytes(_model.SizeBytes);
    public string ModifiedDisplay => _model.ModifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string ScoreDisplay => $"{Math.Min(100, Math.Max(1, (int)(_model.Score * 10)))}%";
    public string OcrConfidenceDisplay => _model.OcrConfidence.HasValue ? $"OCR: {(int)(_model.OcrConfidence.Value * 100)}%" : (IsOcrMatch ? "OCR Match" : string.Empty);
    public string OcrLanguageBadge => !string.IsNullOrEmpty(_model.OcrLanguage) ? _model.OcrLanguage.ToUpperInvariant() : string.Empty;

    public string LocationBadge => !string.IsNullOrEmpty(_model.LocationReference)
        ? _model.LocationReference
        : (_model.PageNumber.HasValue ? $"Page {_model.PageNumber}" :
           (!string.IsNullOrEmpty(_model.SheetName) ? $"{_model.SheetName} ({_model.CellAddress})" :
            (_model.SlideNumber.HasValue ? $"Slide {_model.SlideNumber}" : (IsOcrMatch ? "OCR Image" : "Document"))));

    public string TypeBadge => _model.Extension.TrimStart('.').ToUpperInvariant();

    public string CleanSnippet
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_model.Snippet)) return "No snippet preview available.";
            // Remove mark tags for plain text display or keep for rich text
            return _model.Snippet.Replace("<mark>", "").Replace("</mark>", "");
        }
    }

    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public SearchResultViewModel(SearchResult model)
    {
        _model = model;

        OpenFileCommand = new RelayCommand(() =>
        {
            try
            {
                if (File.Exists(_model.FullPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _model.FullPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open file:\n{ex.Message}", "FileLens", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });

        OpenFolderCommand = new RelayCommand(() =>
        {
            try
            {
                if (File.Exists(_model.FullPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{_model.FullPath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open folder:\n{ex.Message}", "FileLens", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
}

