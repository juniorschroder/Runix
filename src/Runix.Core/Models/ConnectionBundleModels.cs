using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Runix.Core.Models;

public sealed class RunixConnectionBundle
{
    public string FormatVersion { get; set; } = "rcf-v1";
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string ExportedBy { get; set; } = Environment.UserName;
    public List<ConnectionConfig> Connections { get; set; } = new();
    public List<SavedQuery> SavedQueries { get; set; } = new();
    public List<CustomSnippet> CustomSnippets { get; set; } = new();
}

public sealed class BundleCheckItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public object? Payload { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SelectableBundleItem<T> : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public required T Item { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
