using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Desktop.Services;
using Runix.Infrastructure.Data;

namespace Runix.Desktop.Views;

public partial class VisualQueryBuilderTabControl : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly IVisualQueryBuilderService _builderService = new VisualQueryBuilderService();
    private List<TableInfo> _tables = new();
    private List<ForeignKeyInfo> _foreignKeys = new();

    private readonly ObservableCollection<VisualSelectColumn> _selectColumns = new();
    private readonly ObservableCollection<VisualJoinClause> _joins = new();
    private readonly ObservableCollection<VisualFilterCondition> _filters = new();
    private readonly ObservableCollection<VisualSortClause> _sorts = new();
    private readonly ObservableCollection<string> _availableColumns = new();

    public event Action<string>? OpenInQueryEditorRequested;

    public VisualQueryBuilderTabControl()
        : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public VisualQueryBuilderTabControl(IDatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;

        SqlSyntaxHighlighting.ConfigureSqlEditor(TxtGeneratedSql);

        ItemsSelectColumns.ItemsSource = _selectColumns;
        ItemsJoins.ItemsSource = _joins;
        ItemsFilters.ItemsSource = _filters;
        ItemsSorts.ItemsSource = _sorts;
        CmbFilterColumn.ItemsSource = _availableColumns;
        CmbSortColumn.ItemsSource = _availableColumns;

        SetupEvents();
        _ = LoadMetadataAsync();
    }

    private void SetupEvents()
    {
        CmbBaseTable.SelectionChanged += async (s, e) =>
        {
            if (CmbBaseTable.SelectedItem is string tableName && !string.IsNullOrWhiteSpace(tableName))
            {
                await OnBaseTableSelectedAsync(tableName);
            }
        };

        ChkDistinct.IsCheckedChanged += (s, e) => RebuildSqlPreview();
        TxtLimit.TextChanged += (s, e) => RebuildSqlPreview();

        BtnSelectAllCols.Click += (s, e) =>
        {
            foreach (var col in _selectColumns) col.IsSelected = true;
            RebuildSqlPreview();
        };

        BtnClearAllCols.Click += (s, e) =>
        {
            foreach (var col in _selectColumns) col.IsSelected = false;
            RebuildSqlPreview();
        };

        BtnAddJoin.Click += async (s, e) =>
        {
            if (CmbBaseTable.SelectedItem is not string baseTable ||
                CmbJoinTable.SelectedItem is not string joinTable ||
                string.IsNullOrWhiteSpace(joinTable))
                return;

            var joinType = (CmbJoinType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INNER JOIN";
            var suggested = _builderService.SuggestJoinClause(baseTable, baseTable, joinTable, joinTable, _foreignKeys);
            suggested.JoinType = joinType;
            suggested.PropertyChanged += (_, _) => RebuildSqlPreview();
            _joins.Add(suggested);

            await AppendTableColumnsAsync(joinTable);
            RebuildSqlPreview();
        };

        BtnAddFilter.Click += (s, e) =>
        {
            var colEntry = CmbFilterColumn.SelectedItem as string ?? _availableColumns.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(colEntry)) return;

            var (alias, colName) = SplitQualifiedColumn(colEntry);
            var filter = new VisualFilterCondition
            {
                LogicalOperator = (CmbFilterLogic.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "AND",
                TableAlias = alias,
                ColumnName = colName,
                ComparisonOperator = (CmbFilterOperator.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "=",
                Value = TxtFilterValue.Text ?? string.Empty
            };
            filter.PropertyChanged += (_, _) => RebuildSqlPreview();
            _filters.Add(filter);
            TxtFilterValue.Text = string.Empty;
            RebuildSqlPreview();
        };

        BtnAddSort.Click += (s, e) =>
        {
            var colEntry = CmbSortColumn.SelectedItem as string ?? _availableColumns.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(colEntry)) return;

            var (alias, colName) = SplitQualifiedColumn(colEntry);
            var sort = new VisualSortClause
            {
                TableAlias = alias,
                ColumnName = colName,
                Direction = (CmbSortDir.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ASC"
            };
            sort.PropertyChanged += (_, _) => RebuildSqlPreview();
            _sorts.Add(sort);
            RebuildSqlPreview();
        };

        ItemsJoins.AddHandler(Button.ClickEvent, (object? sender, RoutedEventArgs e) =>
        {
            if (e.Source is Button btn && btn.Name == "BtnRemoveJoin" && btn.DataContext is VisualJoinClause join)
            {
                _joins.Remove(join);
                RebuildSqlPreview();
                e.Handled = true;
            }
        });

        ItemsFilters.AddHandler(Button.ClickEvent, (object? sender, RoutedEventArgs e) =>
        {
            if (e.Source is Button btn && btn.Name == "BtnRemoveFilter" && btn.DataContext is VisualFilterCondition filter)
            {
                _filters.Remove(filter);
                RebuildSqlPreview();
                e.Handled = true;
            }
        });

        ItemsSorts.AddHandler(Button.ClickEvent, (object? sender, RoutedEventArgs e) =>
        {
            if (e.Source is Button btn && btn.Name == "BtnRemoveSort" && btn.DataContext is VisualSortClause sort)
            {
                _sorts.Remove(sort);
                RebuildSqlPreview();
                e.Handled = true;
            }
        });

        BtnRunVisualQuery.Click += async (s, e) => await ExecuteVisualQueryAsync();

        BtnOpenInEditor.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(TxtGeneratedSql.Text))
            {
                OpenInQueryEditorRequested?.Invoke(TxtGeneratedSql.Text);
            }
        };

        BtnCopySql.Click += async (s, e) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null && !string.IsNullOrWhiteSpace(TxtGeneratedSql.Text))
            {
                await top.Clipboard.SetTextAsync(TxtGeneratedSql.Text);
                TxtStatusSummary.Text = "✓ SQL copiado para a área de transferência.";
            }
        };

        BtnResetBuilder.Click += async (s, e) =>
        {
            _joins.Clear();
            _filters.Clear();
            _sorts.Clear();
            ChkDistinct.IsChecked = false;
            TxtLimit.Text = "100";
            if (CmbBaseTable.SelectedItem is string tableName)
            {
                await OnBaseTableSelectedAsync(tableName);
            }
        };
    }

    private async Task LoadMetadataAsync()
    {
        try
        {
            _tables = await _databaseService.GetTablesAsync();
            _foreignKeys = await _databaseService.GetForeignKeysAsync();
            TxtFkCountBadge.Text = $"🔗 {_foreignKeys.Count} FKs mapeadas para Auto-Join";

            var tableNames = _tables.Select(t => t.Name).ToList();
            CmbBaseTable.ItemsSource = tableNames;
            CmbJoinTable.ItemsSource = tableNames;

            if (tableNames.Count > 0)
            {
                CmbBaseTable.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            TxtStatusSummary.Text = $"Erro ao carregar schema: {ex.Message}";
        }
    }

    private async Task OnBaseTableSelectedAsync(string tableName)
    {
        _selectColumns.Clear();
        _joins.Clear();
        _filters.Clear();
        _sorts.Clear();
        _availableColumns.Clear();

        await AppendTableColumnsAsync(tableName);
        if (_availableColumns.Count > 0)
        {
            CmbFilterColumn.SelectedIndex = 0;
            CmbSortColumn.SelectedIndex = 0;
        }

        RebuildSqlPreview();
    }

    private async Task AppendTableColumnsAsync(string tableName)
    {
        var cols = await _databaseService.GetTableColumnsAsync(tableName);
        foreach (var c in cols)
        {
            var item = new VisualSelectColumn
            {
                IsSelected = true,
                TableAlias = tableName,
                ColumnName = c.Name,
                DataType = c.DataType
            };
            item.PropertyChanged += (_, _) => RebuildSqlPreview();
            _selectColumns.Add(item);
            _availableColumns.Add($"{tableName}.{c.Name}");
        }
    }

    public void RebuildSqlPreview()
    {
        var baseTable = CmbBaseTable.SelectedItem as string ?? string.Empty;
        int? limit = int.TryParse(TxtLimit.Text, out var parsedLimit) && parsedLimit > 0 ? parsedLimit : null;

        var model = new VisualQueryModel
        {
            BaseTable = baseTable,
            BaseTableAlias = string.Empty,
            Distinct = ChkDistinct.IsChecked == true,
            Limit = limit,
            SelectColumns = _selectColumns.ToList(),
            Joins = _joins.ToList(),
            Filters = _filters.ToList(),
            Sorts = _sorts.ToList()
        };

        // If no joins are used, omit table prefix on single-table query for extra clean SQL
        if (_joins.Count == 0)
        {
            model.SelectColumns = _selectColumns.Select(c => new VisualSelectColumn
            {
                IsSelected = c.IsSelected,
                TableAlias = string.Empty,
                ColumnName = c.ColumnName,
                AggregateFunction = c.AggregateFunction,
                OutputAlias = c.OutputAlias
            }).ToList();
        }

        TxtGeneratedSql.Text = _builderService.BuildSql(model, _databaseService.Config.Type);
    }

    public async Task ExecuteVisualQueryAsync()
    {
        var sql = TxtGeneratedSql.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sql) || sql.StartsWith("--")) return;

        BtnRunVisualQuery.IsEnabled = false;
        TxtStatusSummary.Text = "Executando consulta...";

        try
        {
            var res = await _databaseService.ExecuteQueryAsync(sql, 1000);
            if (!res.Success)
            {
                TxtStatusSummary.Text = $"✖ Erro: {res.ErrorMessage}";
                TxtStatusSummary.Foreground = Brush.Parse("#f87171");
                return;
            }

            GridResults.Columns.Clear();
            for (int i = 0; i < res.Columns.Count; i++)
            {
                var idx = i;
                GridResults.Columns.Add(new DataGridTextColumn
                {
                    Header = res.Columns[i],
                    Binding = new Binding($"[{idx}]")
                });
            }

            GridResults.ItemsSource = res.Rows;
            TxtEmptyResultsHint.IsVisible = res.Rows.Count == 0;
            TxtStatusSummary.Text = $"✓ {res.Rows.Count} linhas retornadas em {res.ElapsedMilliseconds} ms";
            TxtStatusSummary.Foreground = Brush.Parse("#34d399");
        }
        finally
        {
            BtnRunVisualQuery.IsEnabled = true;
        }
    }

    private static (string Alias, string Column) SplitQualifiedColumn(string entry)
    {
        var dot = entry.IndexOf('.');
        if (dot > 0 && dot < entry.Length - 1)
        {
            return (entry[..dot], entry[(dot + 1)..]);
        }
        return (string.Empty, entry);
    }
}
