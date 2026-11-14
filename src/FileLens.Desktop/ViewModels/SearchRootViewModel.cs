using System.ComponentModel;
using System.Runtime.CompilerServices;
using FileLens.Domain.Models;

namespace FileLens.Desktop.ViewModels;

public sealed class SearchRootViewModel : INotifyPropertyChanged
{
    private readonly SearchRoot _model;

    public SearchRoot Model => _model;
    public SearchRootId Id => _model.Id;
    public string Path => _model.Path;
    public RootType RootType => _model.RootType;
    public string TypeDisplay => _model.RootType switch
    {
        RootType.LocalDrive => "Local Drive",
        RootType.UncShare => "Network Share (UNC)",
        _ => "Local Folder"
    };

    public bool IsEnabled
    {
        get => _model.IsEnabled;
        set
        {
            if (_model.IsEnabled != value)
            {
                _model.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public RootStatus Status
    {
        get => _model.Status;
        set
        {
            if (_model.Status != value)
            {
                _model.Status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    public string StatusDisplay => Status.ToString();

    public int FileCount
    {
        get => _model.FileCount;
        set
        {
            if (_model.FileCount != value)
            {
                _model.FileCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastScanDisplay => _model.LastScanUtc.HasValue
        ? _model.LastScanUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "Never scanned";

    public SearchRootViewModel(SearchRoot model)
    {
        _model = model;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(LastScanDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

