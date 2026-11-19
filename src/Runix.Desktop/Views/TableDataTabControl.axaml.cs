namespace Runix.Desktop.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Desktop.Models;
using Runix.Desktop.Services;
using Runix.Infrastructure.Data;

public partial class TableDataTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly string _tableName;
    private int _currentPage = 1;
    private const int PageSize = 100;
    private QueryResult? _lastResult;

    private readonly ObservableCollection<ObservableTableRow> _rows = new();
    private List<string> _columns = new();
    private List<string> _primaryKeys = new();

    private StagedChangeset _changeset;
    private readonly IDmlGeneratorService _dmlGenerator;

    private readonly Dictionary<string, ForeignKeyInfo> _foreignKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly IColumnFilterService _columnFilterService = ColumnFilterService.Instance;
    private readonly Dictionary<string, TextBox> _columnFilterInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Column, bool Ascending)> _sortOrders = new();
    private (string TargetTable, string TargetColumn, string Value)? _pendingFkNavigation;
    private bool _isShiftPressed;
    private bool _isLoadingData;

    public string TableName => _tableName;
    public string? GetFilter() => TxtFilter.Text;
    public void SetFilter(string filter)
    {
        TxtFilter.Text = filter;
        _currentPage = 1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadData());
    }

    public event Action<string>? ViewStructureRequested;
    public event Action<string, string>? NavigateToForeignKeyRequested;

    public TableDataTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()), "")
    {
    }

    public TableDataTabControl(IDatabaseService databaseService, string tableName) : this(databaseService, tableName, null)
    {
    }

    public TableDataTabControl(IDatabaseService databaseService, string tableName, string? initialFilter)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _tableName = tableName;
        TxtTableName.Text = tableName;

        if (!string.IsNullOrWhiteSpace(initialFilter))
        {
            TxtFilter.Text = initialFilter;
        }

        _changeset = new StagedChangeset
        {
            TableName = tableName,
            PrimaryKeyColumns = Array.Empty<string>()
        };

        _dmlGenerator = _databaseService.Config.Type == DatabaseType.PostgreSQL
            ? new PostgresDmlGeneratorService()
            : new SqliteDmlGeneratorService();

        GridTableRows.ItemsSource = _rows;

        // Toolbar Events
        BtnApplyFilter.Click += (s, e) => { _currentPage = 1; LoadData(); };
        BtnToggleColumnFilters.Click += (s, e) => ToggleColumnFiltersBar();
        BtnApplyInlineFilters.Click += (s, e) => ApplyInlineFilters();
        BtnClearInlineFilters.Click += (s, e) => ClearInlineFilters();
        BtnRefresh.Click += (s, e) => LoadData();
        BtnPrevPage.Click += (s, e) => { if (_currentPage > 1) { _currentPage--; LoadData(); } };
        BtnNextPage.Click += (s, e) => { _currentPage++; LoadData(); };
        BtnExportData.Click += (s, e) => OpenExportDialog();
        BtnImportData.Click += (s, e) => OpenImportDialog();
        BtnToggleChart.Click += (s, e) => ToggleChartView();
        BtnToggleInspector.Click += (s, e) => ToggleInspectorDrawer();
        BtnViewStructure.Click += (s, e) => ViewStructureRequested?.Invoke(_tableName);

        // FK Quick Nav Events
        BtnQuickNavigateFk.Click += (s, e) => OnNavigateForeignKeyClicked();
        BtnCloseFkQuickNav.Click += (s, e) => BarForeignKeyQuickNav.IsVisible = false;

        DrawerInspector.SetEditable(true);
        DrawerInspector.CloseRequested += () =>
        {
            DrawerInspector.IsVisible = false;
            BtnToggleInspector.Classes.Clear();
            BtnToggleInspector.Classes.Add("btn-outline");
        };
        DrawerInspector.CellValueApplied += OnInspectorCellValueApplied;

        GridTableRows.SelectionChanged += (s, e) =>
        {
            if (DrawerInspector.IsVisible)
            {
                SyncSelectedRowToInspector();
            }
            CheckAndDisplayForeignKeyQuickNav();
        };

        // Row Editing & Staging Actions
        BtnAddRow.Click += OnAddRow;
        BtnDeleteRow.Click += OnDeleteRow;
        BtnDiscardChanges.Click += OnDiscardChanges;
        BtnCommitChanges.Click += OnCommitChanges;

        // Grid Edit Events
        GridTableRows.CellEditEnded += OnCellEditEnded;
        GridTableRows.LoadingRow += OnLoadingRow;
        GridTableRows.DoubleTapped += (s, e) =>
        {
            if (_pendingFkNavigation.HasValue)
            {
                OnNavigateForeignKeyClicked();
                return;
            }

            if (!_databaseService.Config.IsReadOnly)
            {
                GridTableRows.BeginEdit();
            }
        };
        GridTableRows.KeyDown += (s, e) =>
        {
            if (e.Key is Avalonia.Input.Key.F2 or Avalonia.Input.Key.Enter)
            {
                if (!_databaseService.Config.IsReadOnly)
                {
                    GridTableRows.BeginEdit();
                }
            }
        };

        if (_databaseService.Config.IsReadOnly)
        {
            GridTableRows.IsReadOnly = true;
            BtnAddRow.IsEnabled = false;
            BtnDeleteRow.IsEnabled = false;
            BtnImportData.IsEnabled = false;
            ToolTip.SetTip(BtnAddRow, "Bloqueado no Modo Somente-Leitura (Read-Only Connection Lock)");
            ToolTip.SetTip(BtnDeleteRow, "Bloqueado no Modo Somente-Leitura (Read-Only Connection Lock)");
            ToolTip.SetTip(BtnImportData, "Bloqueado no Modo Somente-Leitura (Read-Only Connection Lock)");
            DrawerInspector.SetEditable(false);
        }

        TxtFilter.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                _currentPage = 1;
                LoadData();
            }
        };

        // Multi-column sorting
        GridTableRows.Sorting += OnGridSorting;
        KeyDown += (s, e) =>
        {
            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            {
                _isShiftPressed = true;
            }
        };
        KeyUp += (s, e) =>
        {
            if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
            {
                _isShiftPressed = false;
            }
        };

        // Context Menu (Copy Cell Value & FK Navigation & Quick Filter)
        MenuGridCell.Opened += (s, e) => UpdateGridContextMenu();
        MenuCopyCellValue.Click += async (s, e) => await CopySelectedCellValueAsync();
        MenuFilterByCellValue.Click += (s, e) => OnFilterBySelectedCellValue();
        MenuNavigateForeignKey.Click += (s, e) => OnNavigateForeignKeyClicked();

        LoadData();
    }

    public async void LoadData()
    {
        if (_isLoadingData) return;
        _isLoadingData = true;
        ProgressLoading.IsVisible = true;

        try
        {
            var offset = (_currentPage - 1) * PageSize;
            var filter = TxtFilter.Text;

            // Discover primary keys for the table if not cached
            if (_primaryKeys.Count == 0)
            {
                try
                {
                    var schemaCols = await _databaseService.GetTableColumnsAsync(_tableName);
                    _primaryKeys = schemaCols.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                    _changeset = new StagedChangeset
                    {
                        TableName = _tableName,
                        PrimaryKeyColumns = _primaryKeys
                    };

                    TxtWarningNoPk.IsVisible = _primaryKeys.Count == 0;
                }
                catch
                {
                    // Ignore schema column discovery errors on view or mock
                }
            }

            // Discover foreign keys if not cached
            if (_foreignKeys.Count == 0)
            {
                try
                {
                    var fks = await _databaseService.GetForeignKeysAsync(_tableName);
                    foreach (var fk in fks)
                    {
                        if (!string.IsNullOrWhiteSpace(fk.SourceColumn))
                        {
                            _foreignKeys[fk.SourceColumn] = fk;
                        }
                    }
                }
                catch
                {
                    // Ignore foreign key discovery errors
                }
            }

            var sortClause = _sortOrders.Count > 0 ? _columnFilterService.BuildOrderByClause(_sortOrders) : null;
            var result = await _databaseService.GetTableDataAsync(_tableName, offset, PageSize, filter, sortClause);
            _lastResult = result;

            if (!result.Success)
            {
                TxtPaginationInfo.Text = $"Error: {result.ErrorMessage}";
                return;
            }

            var pageLabel = LocalizationManager.GetString("TableData.Page", "Page");
            TxtPageNumber.Text = $"{pageLabel} {_currentPage}";

            var pattern = LocalizationManager.GetString("TableData.ShowingRows", "Showing {0} rows");
            TxtPaginationInfo.Text = $"{string.Format(pattern, result.Rows.Count)} (offset {offset}) in {result.ElapsedMilliseconds:F1}ms";
            BtnPrevPage.IsEnabled = _currentPage > 1;
            BtnNextPage.IsEnabled = result.Rows.Count == PageSize;

            _columns = result.Columns;

            // Reutiliza colunas existentes para evitar recriação destrutiva durante ordenação repetida
            if (GridTableRows.Columns.Count == result.Columns.Count)
            {
                for (int i = 0; i < result.Columns.Count; i++)
                {
                    var colName = result.Columns[i];
                    var isPk = _primaryKeys.Contains(colName, StringComparer.OrdinalIgnoreCase);
                    var isFk = _foreignKeys.ContainsKey(colName);
                    var prefix = isPk ? "🔑 " : (isFk ? "🔗 " : "");

                    var sortIdx = _sortOrders.FindIndex(s => string.Equals(s.Column, colName, StringComparison.OrdinalIgnoreCase));
                    var sortIndicator = "";
                    if (sortIdx >= 0)
                    {
                        var isAsc = _sortOrders[sortIdx].Ascending;
                        var arrow = isAsc ? "▲" : "▼";
                        sortIndicator = _sortOrders.Count > 1 ? $" {arrow}{sortIdx + 1}" : $" {arrow}";
                    }

                    GridTableRows.Columns[i].Header = $"{prefix}{colName}{sortIndicator}";
                    GridTableRows.Columns[i].CustomSortComparer = new SafeRowComparer(i);
                }
            }
            else
            {
                GridTableRows.Columns.Clear();
                for (int i = 0; i < result.Columns.Count; i++)
                {
                    var colIndex = i;
                    var colName = result.Columns[i];
                    var isPk = _primaryKeys.Contains(colName, StringComparer.OrdinalIgnoreCase);
                    var isFk = _foreignKeys.ContainsKey(colName);
                    var prefix = isPk ? "🔑 " : (isFk ? "🔗 " : "");

                    var sortIdx = _sortOrders.FindIndex(s => string.Equals(s.Column, colName, StringComparison.OrdinalIgnoreCase));
                    var sortIndicator = "";
                    if (sortIdx >= 0)
                    {
                        var isAsc = _sortOrders[sortIdx].Ascending;
                        var arrow = isAsc ? "▲" : "▼";
                        sortIndicator = _sortOrders.Count > 1 ? $" {arrow}{sortIdx + 1}" : $" {arrow}";
                    }

                    var col = new DataGridTextColumn
                    {
                        Header = $"{prefix}{colName}{sortIndicator}",
                        Tag = colName,
                        Binding = new Binding($"[{colIndex}]")
                        {
                            Mode = BindingMode.TwoWay
                        },
                        CanUserSort = true,
                        CustomSortComparer = new SafeRowComparer(colIndex),
                        IsReadOnly = false,
                        FontSize = 11.5
                    };
                    GridTableRows.Columns.Add(col);
                }
            }

            BuildInlineFilterInputs(result.Columns);

            // Populate rows
            _rows.Clear();
            _changeset.Clear();
            UpdateStagingBar();

            for (int i = 0; i < result.Rows.Count; i++)
            {
                var rowItem = new ObservableTableRow(i, _columns, result.Rows[i]);
                _rows.Add(rowItem);
            }

            if (!ReferenceEquals(GridTableRows.ItemsSource, _rows))
            {
                GridTableRows.ItemsSource = _rows;
            }
        }
        catch (Exception ex)
        {
            TxtPaginationInfo.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _isLoadingData = false;
            ProgressLoading.IsVisible = false;
        }
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is ObservableTableRow row)
        {
            UpdateRowVisualState(e.Row, row);
            row.PropertyChanged += (rs, re) => UpdateRowVisualState(e.Row, row);
        }
    }

    private static void UpdateRowVisualState(DataGridRow gridRow, ObservableTableRow row)
    {
        gridRow.Tag = row.State.ToString();
    }

    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (_databaseService.Config.IsReadOnly) return;
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.DataContext is not ObservableTableRow row) return;

        var colName = e.Column.Header?.ToString();
        if (string.IsNullOrWhiteSpace(colName)) return;

        var origVal = row.OriginalValues.TryGetValue(colName, out var o) ? o : null;
        var newVal = row.CurrentValues.TryGetValue(colName, out var n) ? n : null;

        if (row.State != RowEditState.Added && row.State != RowEditState.Deleted)
        {
            _changeset.TrackCellEdit(row.RowIndex, colName, origVal, newVal);
        }

        UpdateRowVisualState(e.Row, row);
        UpdateStagingBar();
    }

    private void OnAddRow(object? sender, RoutedEventArgs e)
    {
        if (_databaseService.Config.IsReadOnly) return;
        var newValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in _columns)
        {
            newValues[col] = null;
        }

        var newRow = new ObservableTableRow(_rows.Count, _columns, newValues, RowEditState.Added);
        _rows.Add(newRow);
        _changeset.AddRow(newValues);

        GridTableRows.SelectedItem = newRow;
        GridTableRows.ScrollIntoView(newRow, null);

        UpdateStagingBar();
    }

    private void OnDeleteRow(object? sender, RoutedEventArgs e)
    {
        if (_databaseService.Config.IsReadOnly) return;
        if (GridTableRows.SelectedItem is not ObservableTableRow selectedRow) return;

        if (selectedRow.State == RowEditState.Added)
        {
            _rows.Remove(selectedRow);
            // Re-sync added rows in changeset
            _changeset.AddedRows.Clear();
            foreach (var r in _rows.Where(r => r.State == RowEditState.Added))
            {
                _changeset.AddRow(r.CurrentValues);
            }
        }
        else if (selectedRow.State == RowEditState.Deleted)
        {
            // Unmark delete
            selectedRow.State = selectedRow.DirtyColumns.Count > 0 ? RowEditState.Modified : RowEditState.Unchanged;
            _changeset.DeletedRows.RemoveAll(d => MatchesRowKey(d, selectedRow));
        }
        else
        {
            // Mark for deletion
            selectedRow.State = RowEditState.Deleted;
            _changeset.MarkRowDeleted(selectedRow.CurrentValues);
        }

        UpdateStagingBar();
    }

    private bool MatchesRowKey(Dictionary<string, object?> dict, ObservableTableRow row)
    {
        if (_primaryKeys.Count > 0)
        {
            return _primaryKeys.All(pk =>
                dict.TryGetValue(pk, out var v1) &&
                row.CurrentValues.TryGetValue(pk, out var v2) &&
                Equals(v1, v2));
        }
        return dict.All(kvp => row.CurrentValues.TryGetValue(kvp.Key, out var v) && Equals(kvp.Value, v));
    }

    private void OnDiscardChanges(object? sender, RoutedEventArgs e)
    {
        _changeset.Clear();

        // Remove added rows
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            if (_rows[i].State == RowEditState.Added)
            {
                _rows.RemoveAt(i);
            }
            else
            {
                _rows[i].Revert();
            }
        }

        TxtStatusBanner.Text = "";
        UpdateStagingBar();
    }

    private async void OnCommitChanges(object? sender, RoutedEventArgs e)
    {
        if (_databaseService.Config.IsReadOnly)
        {
            TxtStatusBanner.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
            TxtStatusBanner.Text = "Bloqueado: A conexão está configurada como Somente Leitura.";
            return;
        }

        if (!_changeset.HasChanges) return;

        ProgressLoading.IsVisible = true;
        TxtStatusBanner.Text = "";
        BtnCommitChanges.IsEnabled = false;
        BtnDiscardChanges.IsEnabled = false;

        try
        {
            // Sync current values for added rows
            _changeset.AddedRows.Clear();
            foreach (var added in _rows.Where(r => r.State == RowEditState.Added))
            {
                _changeset.AddRow(added.CurrentValues);
            }

            // Build original row dictionaries for PK target resolution
            var originalDicts = _rows
                .Where(r => r.State != RowEditState.Added)
                .Select(r => r.OriginalValues as IReadOnlyDictionary<string, object?>)
                .ToList();

            var batch = _dmlGenerator.GenerateBatch(_changeset, originalDicts);
            var result = await _databaseService.ExecuteBatchTransactionalAsync(batch.Commands);

            ProgressLoading.IsVisible = false;
            BtnCommitChanges.IsEnabled = true;
            BtnDiscardChanges.IsEnabled = true;

            if (result.Success)
            {
                _changeset.Clear();
                UpdateStagingBar();

                var successPattern = LocalizationManager.GetString("TableData.SaveSuccess", "Changes committed successfully ({0} rows affected).");
                TxtStatusBanner.Foreground = new SolidColorBrush(Color.Parse("#34d399"));
                TxtStatusBanner.Text = string.Format(successPattern, result.AffectedRows);

                // Reload fresh table data
                LoadData();
            }
            else
            {
                var errorPattern = LocalizationManager.GetString("TableData.SaveError", "Error saving changes: {0}");
                TxtStatusBanner.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
                TxtStatusBanner.Text = string.Format(errorPattern, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ProgressLoading.IsVisible = false;
            BtnCommitChanges.IsEnabled = true;
            BtnDiscardChanges.IsEnabled = true;

            var errorPattern = LocalizationManager.GetString("TableData.SaveError", "Error saving changes: {0}");
            TxtStatusBanner.Foreground = new SolidColorBrush(Color.Parse("#f87171"));
            TxtStatusBanner.Text = string.Format(errorPattern, ex.Message);
        }
    }

    private void UpdateStagingBar()
    {
        if (_changeset.HasChanges)
        {
            PanelStagingBar.IsVisible = true;
            var pattern = LocalizationManager.GetString("TableData.StagedChanges", "{0} pending changes ({1} modified, {2} added, {3} deleted)");
            TxtStagedCount.Text = string.Format(
                pattern,
                _changeset.TotalChangesCount,
                _changeset.ModifiedRowsCount,
                _changeset.AddedRowsCount,
                _changeset.DeletedRowsCount);
        }
        else
        {
            PanelStagingBar.IsVisible = false;
        }
    }

    public void OpenExportDialog()
    {
        var rawRows = new List<IReadOnlyList<object?>>();
        foreach (var row in _rows)
        {
            var vals = new object?[_columns.Count];
            for (int i = 0; i < _columns.Count; i++)
            {
                vals[i] = row.GetValue(_columns[i]);
            }
            rawRows.Add(vals);
        }

        var dialog = new Dialogs.ExportDialog();
        dialog.SetupForTable(_tableName, _columns, rawRows, _databaseService);

        dialog.CancelRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.ExportCompleted += result =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
            TxtStatusBanner.Text = $"✓ Exportadas {result.RowsExported:N0} linhas ({result.FileSizeFormatted}) para {Path.GetFileName(result.FilePath)}";
            TxtStatusBanner.Foreground = Avalonia.Media.Brush.Parse("#34d399");
            PanelStagingBar.IsVisible = true;
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public Dialogs.ImportWizardDialog OpenImportDialog(string? preloadedFilePath = null)
    {
        var dialog = new Dialogs.ImportWizardDialog();
        var cols = _columns.Select(c => new ColumnInfo { Name = c, DataType = "TEXT" }).ToList();
        dialog.SetContext(_databaseService, _tableName, cols);

        if (!string.IsNullOrEmpty(preloadedFilePath))
        {
            dialog.PreloadFile(preloadedFilePath);
        }

        dialog.CancelRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.ImportCompleted += result =>
        {
            if (result.Success)
            {
                PanelModalOverlay.IsVisible = false;
                ModalContainer.Content = null;
                TxtStatusBanner.Text = $"✓ Importadas {result.InsertedRowsCount:N0} linhas ({result.ElapsedFormatted})";
                TxtStatusBanner.Foreground = Avalonia.Media.Brush.Parse("#34d399");
                PanelStagingBar.IsVisible = true;
                LoadData();
            }
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
        return dialog;
    }

    public void EmulateStagedEditsForTesting()
    {
        if (_rows.Count > 0)
        {
            // Edit row 0
            var row0 = _rows[0];
            var origName = row0.GetValue("name");
            row0.SetValue("name", $"{origName} (AOP Gourmet)");
            _changeset.TrackCellEdit(0, "name", origName, row0.GetValue("name"));

            // Mark row 1 as deleted
            if (_rows.Count > 1)
            {
                var row1 = _rows[1];
                row1.State = RowEditState.Deleted;
                _changeset.MarkRowDeleted(row1.CurrentValues);
            }

            // Add a new row
            var newVals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in _columns)
            {
                newVals[col] = col == "name" ? "Queijo da Canastra" : (col == "id" ? 999 : null);
            }
            var newRow = new ObservableTableRow(_rows.Count, _columns, newVals, RowEditState.Added);
            _rows.Add(newRow);
            _changeset.AddRow(newVals);

            UpdateStagingBar();
        }
    }

    private static string EscapeCsv(string field)
    {
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    private bool _isShowingChart;

    public void ToggleChartView()
    {
        _isShowingChart = !_isShowingChart;
        GridTableRows.IsVisible = !_isShowingChart;
        ViewChart.IsVisible = _isShowingChart;

        if (_isShowingChart)
        {
            BtnToggleChart.Classes.Clear();
            BtnToggleChart.Classes.Add("btn-secondary");
            BtnToggleChart.Content = "📊 Grid";

            var rawRows = new List<List<object?>>();
            foreach (var row in _rows)
            {
                var rowCells = new List<object?>();
                for (int i = 0; i < _columns.Count; i++)
                {
                    rowCells.Add(row.GetValue(_columns[i]));
                }
                rawRows.Add(rowCells);
            }

            var qr = QueryResult.CreateSuccess(_columns, rawRows, 0);
            ViewChart.SetData(qr, $"Visualização de Dados: {_tableName}");
        }
        else
        {
            BtnToggleChart.Classes.Clear();
            BtnToggleChart.Classes.Add("btn-outline");
            BtnToggleChart.Content = "📈 Gráfico";
        }
    }

    public void ToggleInspectorDrawer()
    {
        DrawerInspector.IsVisible = !DrawerInspector.IsVisible;
        BtnToggleInspector.Classes.Clear();
        BtnToggleInspector.Classes.Add(DrawerInspector.IsVisible ? "btn-secondary" : "btn-outline");

        if (DrawerInspector.IsVisible)
        {
            if (GridTableRows.SelectedItem == null && _rows.Count > 0)
            {
                GridTableRows.SelectedIndex = 0;
            }
            SyncSelectedRowToInspector();
        }
    }

    public void ApplyFilterClause(string filterSqlClause)
    {
        TxtFilter.Text = filterSqlClause;
        _currentPage = 1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadData());
    }

    private void SyncSelectedRowToInspector()
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row)
        {
            var values = new List<object?>(_columns.Count);
            foreach (var col in _columns)
            {
                values.Add(row.GetValue(col));
            }
            DrawerInspector.LoadRow(_columns, values);
        }
        else
        {
            DrawerInspector.LoadRow(Array.Empty<string>(), Array.Empty<object?>());
        }
    }

    private void OnInspectorCellValueApplied(string columnName, string newValue)
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row)
        {
            var oldValue = row.OriginalValues.TryGetValue(columnName, out var orig) ? orig : null;
            row.SetValue(columnName, newValue);
            if (row.State != RowEditState.Added && row.State != RowEditState.Deleted)
            {
                _changeset.TrackCellEdit(row.RowIndex, columnName, oldValue, newValue);
            }
            UpdateStagingBar();
        }
    }

    public void ToggleColumnFiltersBar()
    {
        BarInlineColumnFilters.IsVisible = !BarInlineColumnFilters.IsVisible;
        BtnToggleColumnFilters.Classes.Clear();
        BtnToggleColumnFilters.Classes.Add(BarInlineColumnFilters.IsVisible ? "btn-secondary" : "btn-outline");

        if (BarInlineColumnFilters.IsVisible && _columnFilterInputs.Count == 0 && _columns.Count > 0)
        {
            BuildInlineFilterInputs(_columns);
        }
    }

    private void BuildInlineFilterInputs(List<string> columns)
    {
        if (columns.Count == 0) return;

        // Check if layout needs rebuild
        var isMatch = _columnFilterInputs.Count == columns.Count && columns.All(c => _columnFilterInputs.ContainsKey(c));
        if (isMatch) return;

        PanelColumnFiltersHost.Children.Clear();
        _columnFilterInputs.Clear();

        foreach (var col in columns)
        {
            var isPk = _primaryKeys.Contains(col, StringComparer.OrdinalIgnoreCase);
            var isFk = _foreignKeys.ContainsKey(col);
            var icon = isPk ? "🔑 " : (isFk ? "🔗 " : "");

            var panel = new StackPanel
            {
                Width = 145,
                Margin = new Avalonia.Thickness(0, 0, 8, 4)
            };

            var header = new TextBlock
            {
                Text = $"{icon}{col}",
                FontSize = 10.5,
                FontWeight = (isPk || isFk) ? FontWeight.Bold : FontWeight.Normal,
                Foreground = isFk ? new SolidColorBrush(Color.Parse("#38bdf8")) : (isPk ? new SolidColorBrush(Color.Parse("#fbbf24")) : new SolidColorBrush(Color.Parse("#94a3b8"))),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Avalonia.Thickness(2, 0, 0, 4)
            };
            panel.Children.Add(header);

            var tb = new TextBox
            {
                Watermark = isFk ? "FK / >10, %abc%" : "ex: >10, %abc%",
                FontSize = 11,
                Padding = new Avalonia.Thickness(6, 4),
                Height = 28
            };

            tb.KeyDown += (s, ev) =>
            {
                if (ev.Key == Avalonia.Input.Key.Enter)
                {
                    ApplyInlineFilters();
                }
            };

            _columnFilterInputs[col] = tb;
            panel.Children.Add(tb);
            PanelColumnFiltersHost.Children.Add(panel);
        }
    }

    private void ApplyInlineFilters()
    {
        var activeFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (col, tb) in _columnFilterInputs)
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                activeFilters[col] = tb.Text.Trim();
            }
        }

        if (activeFilters.Count > 0)
        {
            var whereClause = _columnFilterService.BuildWhereClause(activeFilters);
            TxtFilter.Text = whereClause;
            TxtActiveFiltersCount.Text = $"{activeFilters.Count} ativo{(activeFilters.Count > 1 ? "s" : "")}";
            BadgeActiveFiltersCount.IsVisible = true;
        }
        else
        {
            TxtFilter.Text = "";
            BadgeActiveFiltersCount.IsVisible = false;
        }

        _currentPage = 1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadData());
    }

    private void ClearInlineFilters()
    {
        foreach (var tb in _columnFilterInputs.Values)
        {
            tb.Text = "";
        }

        BadgeActiveFiltersCount.IsVisible = false;
        TxtFilter.Text = "";
        _currentPage = 1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadData());
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;

        var colName = e.Column.Tag as string;
        if (string.IsNullOrWhiteSpace(colName))
        {
            return;
        }

        if (_isShiftPressed)
        {
            var existingIndex = _sortOrders.FindIndex(s => string.Equals(s.Column, colName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                if (_sortOrders[existingIndex].Ascending)
                {
                    _sortOrders[existingIndex] = (colName, false);
                }
                else
                {
                    _sortOrders.RemoveAt(existingIndex);
                }
            }
            else
            {
                _sortOrders.Add((colName, true));
            }
        }
        else
        {
            if (_sortOrders.Count == 1 && string.Equals(_sortOrders[0].Column, colName, StringComparison.OrdinalIgnoreCase))
            {
                if (_sortOrders[0].Ascending)
                {
                    _sortOrders[0] = (colName, false);
                }
                else
                {
                    _sortOrders.Clear();
                }
            }
            else
            {
                _sortOrders.Clear();
                _sortOrders.Add((colName, true));
            }
        }

        _currentPage = 1;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadData());
    }

    private void UpdateGridContextMenu()
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row && GridTableRows.CurrentColumn is { } currentCol)
        {
            var colName = currentCol.Tag as string;
            if (!string.IsNullOrEmpty(colName) && _foreignKeys.TryGetValue(colName, out var fk))
            {
                var val = row.GetValue(colName);
                if (val != null && val != DBNull.Value && !string.IsNullOrWhiteSpace(val.ToString()))
                {
                    var targetCol = string.IsNullOrWhiteSpace(fk.TargetColumn) ? "id" : fk.TargetColumn;
                    _pendingFkNavigation = (fk.TargetTable, targetCol, val.ToString()!);
                    MenuNavigateForeignKey.Header = $"🔗 Ir para {fk.TargetTable} ({targetCol} = {val})";
                    MenuNavigateForeignKey.IsVisible = true;
                    SepFkNavigation.IsVisible = true;
                    return;
                }
            }
        }

        _pendingFkNavigation = null;
        MenuNavigateForeignKey.IsVisible = false;
        SepFkNavigation.IsVisible = false;
    }

    private void CheckAndDisplayForeignKeyQuickNav()
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row)
        {
            var colName = GridTableRows.CurrentColumn?.Tag as string;
            if (!string.IsNullOrEmpty(colName) && _foreignKeys.TryGetValue(colName, out var currentFk))
            {
                var val = row.GetValue(colName);
                if (val != null && val != DBNull.Value && !string.IsNullOrWhiteSpace(val.ToString()))
                {
                    var targetCol = string.IsNullOrWhiteSpace(currentFk.TargetColumn) ? "id" : currentFk.TargetColumn;
                    _pendingFkNavigation = (currentFk.TargetTable, targetCol, val.ToString()!);
                    TxtForeignKeyInfo.Text = $"Chave Estrangeira: {currentFk.SourceColumn} = {val}  →  Tabela {currentFk.TargetTable} ({targetCol})";
                    BtnQuickNavigateFk.Content = $"↗ Ir para {currentFk.TargetTable} ({targetCol} = {val})";
                    BarForeignKeyQuickNav.IsVisible = true;
                    return;
                }
            }

            foreach (var (fkCol, fk) in _foreignKeys)
            {
                var val = row.GetValue(fkCol);
                if (val != null && val != DBNull.Value && !string.IsNullOrWhiteSpace(val.ToString()))
                {
                    var targetCol = string.IsNullOrWhiteSpace(fk.TargetColumn) ? "id" : fk.TargetColumn;
                    _pendingFkNavigation = (fk.TargetTable, targetCol, val.ToString()!);
                    TxtForeignKeyInfo.Text = $"Chave Estrangeira: {fk.SourceColumn} = {val}  →  Tabela {fk.TargetTable} ({targetCol})";
                    BtnQuickNavigateFk.Content = $"↗ Ir para {fk.TargetTable} ({targetCol} = {val})";
                    BarForeignKeyQuickNav.IsVisible = true;
                    return;
                }
            }
        }

        _pendingFkNavigation = null;
        BarForeignKeyQuickNav.IsVisible = false;
    }

    private void OnNavigateForeignKeyClicked()
    {
        if (_pendingFkNavigation.HasValue)
        {
            var nav = _pendingFkNavigation.Value;
            var targetCol = string.IsNullOrWhiteSpace(nav.TargetColumn) ? "id" : nav.TargetColumn;
            string formattedVal;
            if (long.TryParse(nav.Value, out _) || double.TryParse(nav.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                formattedVal = nav.Value;
            }
            else
            {
                formattedVal = $"'{nav.Value.Replace("'", "''")}'";
            }

            var filterClause = $"\"{targetCol}\" = {formattedVal}";
            NavigateToForeignKeyRequested?.Invoke(nav.TargetTable, filterClause);
        }
    }

    private void OnFilterBySelectedCellValue()
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row && GridTableRows.CurrentColumn is { } currentCol)
        {
            var colName = currentCol.Tag as string;
            if (string.IsNullOrEmpty(colName)) return;

            var val = row.GetValue(colName);
            var strVal = val == null || val == DBNull.Value ? "IS NULL" : val.ToString() ?? "";

            BarInlineColumnFilters.IsVisible = true;
            BtnToggleColumnFilters.Classes.Clear();
            BtnToggleColumnFilters.Classes.Add("btn-secondary");

            if (!_columnFilterInputs.ContainsKey(colName) && _columns.Count > 0)
            {
                BuildInlineFilterInputs(_columns);
            }

            if (_columnFilterInputs.TryGetValue(colName, out var tb))
            {
                if (val == null || val == DBNull.Value)
                {
                    tb.Text = "IS NULL";
                }
                else if (long.TryParse(strVal, out _) || double.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    tb.Text = $"= {strVal}";
                }
                else
                {
                    tb.Text = $"= '{strVal}'";
                }

                ApplyInlineFilters();
            }
        }
    }

    private async Task CopySelectedCellValueAsync()
    {
        if (GridTableRows.SelectedItem is ObservableTableRow row)
        {
            var colName = GridTableRows.CurrentColumn?.Tag as string;
            object? val = null;
            if (!string.IsNullOrEmpty(colName))
            {
                val = row.GetValue(colName);
            }
            else
            {
                val = row.CurrentValues.Values.FirstOrDefault(v => v != null);
            }

            var text = val?.ToString() ?? "";
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }

    public void Dispose()
    {
        GridTableRows.ItemsSource = null;
        GridTableRows.Columns.Clear();
        _rows.Clear();
        _lastResult?.Rows.Clear();
        _lastResult = null;
        _columns.Clear();
        _primaryKeys.Clear();
        _foreignKeys.Clear();
        _columnFilterInputs.Clear();
        _changeset.Clear();
    }
}
