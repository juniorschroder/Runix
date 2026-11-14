namespace Runix.Desktop.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Runix.Core.Models;

public class ObservableTableRow : INotifyPropertyChanged
{
    private readonly IReadOnlyList<string> _columns;
    private readonly Dictionary<string, int> _columnIndices;
    private readonly List<object?> _values;
    private RowEditState _state = RowEditState.Unchanged;

    public int RowIndex { get; set; }
    public Dictionary<string, object?> OriginalValues { get; }
    public Dictionary<string, object?> CurrentValues { get; }
    public HashSet<string> DirtyColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public RowEditState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(IsAdded));
                OnPropertyChanged(nameof(IsDeleted));
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(RowStateText));
            }
        }
    }

    public bool IsModified => State == RowEditState.Modified;
    public bool IsAdded => State == RowEditState.Added;
    public bool IsDeleted => State == RowEditState.Deleted;
    public bool IsDirty => DirtyColumns.Count > 0 || State != RowEditState.Unchanged;

    public string RowStateText => State switch
    {
        RowEditState.Modified => "MODIFIED",
        RowEditState.Added => "NEW",
        RowEditState.Deleted => "DELETED",
        _ => ""
    };

    public ObservableTableRow(int rowIndex, IReadOnlyList<string> columns, IReadOnlyList<object?> values, RowEditState initialState = RowEditState.Unchanged)
    {
        RowIndex = rowIndex;
        _columns = columns;
        _state = initialState;
        _columnIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        OriginalValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        CurrentValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        _values = new List<object?>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            _columnIndices[col] = i;
            var val = (i < values.Count) ? values[i] : null;
            if (val is string s && s == "NULL") val = null;

            OriginalValues[col] = val;
            CurrentValues[col] = val;
            _values.Add(val);
        }
    }

    public ObservableTableRow(int rowIndex, IReadOnlyList<string> columns, Dictionary<string, object?> values, RowEditState initialState = RowEditState.Unchanged)
    {
        RowIndex = rowIndex;
        _columns = columns;
        _state = initialState;
        _columnIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        OriginalValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        CurrentValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        _values = new List<object?>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            _columnIndices[col] = i;
            values.TryGetValue(col, out var val);
            if (val is string s && s == "NULL") val = null;

            OriginalValues[col] = val;
            CurrentValues[col] = val;
            _values.Add(val);
        }
    }

    /// <summary>
    /// Single unambiguous indexer Item[Int32] for Avalonia reflection binding engine.
    /// </summary>
    public object? this[int columnIndex]
    {
        get => (columnIndex >= 0 && columnIndex < _values.Count) ? _values[columnIndex] : null;
        set
        {
            if (columnIndex >= 0 && columnIndex < _values.Count)
            {
                _values[columnIndex] = value;
                var colName = _columns[columnIndex];
                CurrentValues[colName] = value;
                var orig = OriginalValues.TryGetValue(colName, out var o) ? o : null;
                var origStr = orig?.ToString() ?? "";
                var valStr = value?.ToString() ?? "";

                if (Equals(orig, value) || origStr == valStr)
                {
                    DirtyColumns.Remove(colName);
                }
                else
                {
                    DirtyColumns.Add(colName);
                }

                if (State != RowEditState.Added && State != RowEditState.Deleted)
                {
                    State = DirtyColumns.Count > 0 ? RowEditState.Modified : RowEditState.Unchanged;
                }

                OnPropertyChanged("Item[]");
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public object? GetValue(string columnName)
    {
        return CurrentValues.TryGetValue(columnName, out var v) ? v : null;
    }

    public void SetValue(string columnName, object? value)
    {
        if (_columnIndices.TryGetValue(columnName, out var idx))
        {
            this[idx] = value;
        }
        else
        {
            CurrentValues[columnName] = value;
        }
    }

    public void Revert()
    {
        if (State == RowEditState.Added) return;

        CurrentValues.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            var col = _columns[i];
            var orig = OriginalValues.TryGetValue(col, out var o) ? o : null;
            CurrentValues[col] = orig;
            if (i < _values.Count)
            {
                _values[i] = orig;
            }
        }
        DirtyColumns.Clear();
        State = RowEditState.Unchanged;

        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(IsDirty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
