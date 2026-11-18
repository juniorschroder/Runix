using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Runix.Core.Models;

namespace Runix.Desktop.Views;

public enum ErdSearchItemType
{
    Table,
    Column
}

public class ErdSearchItem
{
    public ErdSearchItemType Type { get; set; }
    public string TableName { get; set; } = "";
    public string? ColumnName { get; set; }
    public string DisplayText { get; set; } = "";
    public string SecondaryText { get; set; } = "";
    public string Icon => Type == ErdSearchItemType.Table ? "📁" : "🔹";
}

public partial class ErdSearchOverlayControl : UserControl
{
    private ErdGraphModel _graph = new();
    private readonly ObservableCollection<ErdSearchItem> _results = new();

    public event Action<string, string?>? ItemSelected;
    public event Action? Closed;

    public ErdSearchOverlayControl()
    {
        InitializeComponent();
        ListResults.ItemsSource = _results;

        TxtSearch.TextChanged += (s, e) => PerformFilter(TxtSearch.Text);
        BtnClose.Click += (s, e) => Close();

        TxtSearch.KeyDown += OnSearchKeyDown;
        ListResults.KeyDown += OnListKeyDown;
        ListResults.DoubleTapped += (s, e) => ConfirmSelection();
        ListResults.SelectionChanged += (s, e) =>
        {
            if (ListResults.SelectedItem != null && !TxtSearch.IsFocused)
            {
                ConfirmSelection();
            }
        };
    }

    public void SetGraph(ErdGraphModel graph)
    {
        _graph = graph ?? new ErdGraphModel();
        PerformFilter(TxtSearch.Text);
    }

    public void OpenAndFocus()
    {
        IsVisible = true;
        PerformFilter(TxtSearch.Text);
        TxtSearch.Focus();
        TxtSearch.SelectAll();
    }

    public void Close()
    {
        IsVisible = false;
        TxtSearch.Text = string.Empty;
        Closed?.Invoke();
    }

    private void PerformFilter(string? query)
    {
        _results.Clear();

        var q = query?.Trim() ?? string.Empty;

        var allItems = new List<ErdSearchItem>();

        foreach (var node in _graph.Nodes)
        {
            // Table item
            if (string.IsNullOrEmpty(q) || node.TableName.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                allItems.Add(new ErdSearchItem
                {
                    Type = ErdSearchItemType.Table,
                    TableName = node.TableName,
                    DisplayText = node.TableName,
                    SecondaryText = $"{node.Columns.Count} colunas"
                });
            }

            // Columns
            foreach (var col in node.Columns)
            {
                var fullColName = $"{node.TableName}.{col.Name}";
                if (string.IsNullOrEmpty(q) ||
                    col.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    fullColName.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    allItems.Add(new ErdSearchItem
                    {
                        Type = ErdSearchItemType.Column,
                        TableName = node.TableName,
                        ColumnName = col.Name,
                        DisplayText = fullColName,
                        SecondaryText = col.DataType
                    });
                }
            }
        }

        // Limit results to top 40 for responsiveness
        foreach (var item in allItems.Take(40))
        {
            _results.Add(item);
        }

        TxtResultCount.Text = $"{_results.Count} itens";
        TxtNoResults.IsVisible = _results.Count == 0;
        ListResults.IsVisible = _results.Count > 0;

        if (_results.Count > 0)
        {
            ListResults.SelectedIndex = 0;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_results.Count > 0)
            {
                int nextIdx = Math.Min(_results.Count - 1, ListResults.SelectedIndex + 1);
                ListResults.SelectedIndex = nextIdx;
                if (ListResults.SelectedItem != null)
                {
                    ListResults.ScrollIntoView(ListResults.SelectedItem);
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_results.Count > 0)
            {
                int prevIdx = Math.Max(0, ListResults.SelectedIndex - 1);
                ListResults.SelectedIndex = prevIdx;
                if (ListResults.SelectedItem != null)
                {
                    ListResults.ScrollIntoView(ListResults.SelectedItem);
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void ConfirmSelection()
    {
        if (ListResults.SelectedItem is ErdSearchItem item)
        {
            ItemSelected?.Invoke(item.TableName, item.ColumnName);
            Close();
        }
    }
}
