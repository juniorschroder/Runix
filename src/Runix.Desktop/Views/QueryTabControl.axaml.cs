namespace Runix.Desktop.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Desktop.Services;
using Runix.Desktop.Views.Dialogs;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Explain;
using Runix.Infrastructure.Storage;

public partial class QueryTabControl : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly IAppStorageService _appStorage;
    private QueryResult? _lastResult;
    public event Action<QueryHistoryItem>? QueryExecuted;
    public event Action<SavedQuery>? QuerySaved;
    public event Action? SnippetsChanged;

    private readonly ISqlLexerService _lexerService = new SqlLexerService();
    private readonly ISqlCompletionService _completionService = new SqlCompletionService();
    private readonly ISqlFormatterService _formatterService = new SqlFormatterService();
    private readonly ISqlAutoLimitService _autoLimitService = SqlAutoLimitService.Instance;
    private readonly IQueryParameterService _parameterService = QueryParameterService.Instance;
    private readonly Dictionary<string, TextBox> _queryColumnFilterInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly IColumnFilterService _columnFilterService = ColumnFilterService.Instance;
    private QueryResult? _currentActiveResult;

    private List<TableInfo> _cachedTables = new();
    private Dictionary<string, IReadOnlyList<ColumnInfo>> _cachedColumns = new(StringComparer.OrdinalIgnoreCase);
    private List<CustomSnippet> _cachedSnippets = new();

    private int _activeSubResultIndex = 0;
    private bool _isSplitLayoutMode = false;

    private AvaloniaEdit.CodeCompletion.CompletionWindow? _completionWindow;
    private System.Threading.CancellationTokenSource? _queryCts;

    public event Action? SqlTextChanged;

    public int CaretIndex
    {
        get => TxtSqlEditor.CaretOffset;
        set => TxtSqlEditor.CaretOffset = Math.Max(0, Math.Min(value, TxtSqlEditor.Text?.Length ?? 0));
    }

    public double VerticalScrollOffset
    {
        get => TxtSqlEditor.VerticalOffset;
        set => TxtSqlEditor.ScrollToVerticalOffset(value);
    }

    public double HorizontalScrollOffset
    {
        get => TxtSqlEditor.HorizontalOffset;
        set => TxtSqlEditor.ScrollToHorizontalOffset(value);
    }

    public void RestoreEditorPosition(int caretOffset, double verticalOffset, double horizontalOffset)
    {
        if (caretOffset >= 0 && TxtSqlEditor.Text != null)
        {
            CaretIndex = caretOffset;
        }

        if (verticalOffset > 0)
        {
            TxtSqlEditor.ScrollToVerticalOffset(verticalOffset);
        }
        if (horizontalOffset > 0)
        {
            TxtSqlEditor.ScrollToHorizontalOffset(horizontalOffset);
        }

        if (caretOffset > 0 || verticalOffset > 0 || horizontalOffset > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (caretOffset >= 0 && TxtSqlEditor.Text != null) CaretIndex = caretOffset;
                if (verticalOffset > 0) TxtSqlEditor.ScrollToVerticalOffset(verticalOffset);
                if (horizontalOffset > 0) TxtSqlEditor.ScrollToHorizontalOffset(horizontalOffset);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    public QueryTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public QueryTabControl(IDatabaseService databaseService, string? initialSql = null, IAppStorageService? appStorage = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _appStorage = appStorage ?? AppStorageService.Instance;
        QueryParameterStorage.LoadIntoService(_parameterService);

        // Configure modern AvaloniaEdit SQL editor with syntax highlighting & dark theme
        SqlSyntaxHighlighting.ConfigureSqlEditor(TxtSqlEditor);

        if (!string.IsNullOrWhiteSpace(initialSql))
        {
            TxtSqlEditor.Text = initialSql;
        }

        BtnRun.Click += (s, e) => ExecuteSql();
        BtnCancelQuery.Click += (s, e) => CancelQuery();
        BtnExplainPlan.Click += (s, e) => _ = RunExplainAsync(analyze: false);
        MenuExplainQuick.Click += (s, e) => _ = RunExplainAsync(analyze: false);
        MenuExplainAnalyze.Click += (s, e) => _ = RunExplainAsync(analyze: true);
        BtnClear.Click += (s, e) => TxtSqlEditor.Text = string.Empty;
        BtnSaveQuery.Click += (s, e) => OpenSaveQueryDialog();
        BtnFormatSql.Click += (s, e) => FormatSqlInEditor();
        BtnManageSnippets.Click += (s, e) => OpenSnippetManagerDialog();
        BtnExportData.Click += (s, e) => OpenExportDialog();

        BtnTabResults.Click += (s, e) => SwitchBottomTab(0);
        BtnTabChart.Click += (s, e) => SwitchBottomTab(1);
        BtnTabExplain.Click += (s, e) => SwitchBottomTab(2);
        BtnToggleInspector.Click += (s, e) => ToggleInspectorDrawer();
        BtnToggleQueryFilters.Click += (s, e) => ToggleQueryFiltersBar();
        BtnApplyQueryFilters.Click += (s, e) => ApplyQueryFilters();
        BtnClearQueryFilters.Click += (s, e) => ClearQueryFilters();
        MenuQueryFilterByCellValue.Click += (s, e) => OnFilterBySelectedQueryCellValue();
        MenuQueryCopyCellValue.Click += async (s, e) => await CopySelectedQueryCellValueAsync();
        BtnSubLayoutTabs.Click += (s, e) => SetSubResultLayoutMode(isSplit: false);
        BtnSubLayoutSplit.Click += (s, e) => SetSubResultLayoutMode(isSplit: true);

        DrawerInspector.SetEditable(false);
        DrawerInspector.CloseRequested += () =>
        {
            DrawerInspector.IsVisible = false;
            SetTabButtonActive(BtnToggleInspector, false);
        };

        GridResults.SelectionChanged += (s, e) =>
        {
            if (DrawerInspector.IsVisible)
            {
                SyncSelectedRowToInspector();
            }
        };

        ViewExplainPlan.CreateIndexRequested += async ddl =>
        {
            try
            {
                var res = await _databaseService.ExecuteQueryAsync(ddl);
                if (res.Success)
                {
                    TxtExecutionStatus.Text = "✓ Índice criado com sucesso!";
                    TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
                    await RunExplainAsync(analyze: true);
                }
                else
                {
                    TxtExecutionStatus.Text = $"Falha ao criar índice: {res.ErrorMessage}";
                    TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
                }
            }
            catch (Exception ex)
            {
                TxtExecutionStatus.Text = $"Erro: {ex.Message}";
                TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            }
        };

        // Code completion & shortcut events on editor TextArea
        TxtSqlEditor.TextArea.TextEntered += OnTextEntered;
        TxtSqlEditor.TextArea.KeyDown += OnTextAreaKeyDown;
        TxtSqlEditor.TextChanged += (s, e) => SqlTextChanged?.Invoke();

        // Initialize schema cache & custom snippets asynchronously
        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await InitializeSchemaCacheAsync();
        await ReloadCustomSnippetsAsync();
    }

    public async Task ReloadCustomSnippetsAsync()
    {
        try
        {
            var snippets = await _appStorage.GetCustomSnippetsAsync();
            _cachedSnippets = snippets.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryTab] Error loading snippets: {ex.Message}");
        }
    }

    public async Task InitializeSchemaCacheAsync()
    {
        try
        {
            _cachedTables = await _databaseService.GetTablesAsync();
            var colsDict = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var tbl in _cachedTables)
            {
                try
                {
                    var cols = await _databaseService.GetTableColumnsAsync(tbl.Name);
                    colsDict[tbl.Name] = cols;
                }
                catch
                {
                    // Ignore individual table inspection errors
                }
            }

            _cachedColumns = colsDict;
        }
        catch
        {
            // Ignore schema loading error on disconnected state
        }
    }

    public void SetSqlText(string sql)
    {
        TxtSqlEditor.Text = sql;
        TxtSqlEditor.CaretOffset = sql.Length;
    }

    public string GetSqlText() => TxtSqlEditor.Text ?? string.Empty;

    public void OpenSaveQueryDialog(SavedQuery? existing = null)
    {
        var sql = TxtSqlEditor.Text ?? string.Empty;
        var dialog = new SaveQueryDialog();
        var connId = _databaseService.Config.Name ?? _databaseService.Config.Database;
        dialog.Setup(sql, connId, existing);

        dialog.CancelRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.SaveRequested += async saved =>
        {
            try
            {
                await _appStorage.SaveQueryAsync(saved);
                PanelModalOverlay.IsVisible = false;
                ModalContainer.Content = null;
                TxtExecutionStatus.Text = $"✓ Consulta \"{saved.Title}\" salva!";
                TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
                QuerySaved?.Invoke(saved);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QueryTab] Error saving query: {ex.Message}");
            }
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public void OpenSnippetManagerDialog()
    {
        var dialog = new SnippetManagerDialog();
        dialog.Setup(_appStorage);

        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.SnippetsUpdated += async () =>
        {
            await ReloadCustomSnippetsAsync();
            SnippetsChanged?.Invoke();
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
        {
            char c = e.Text[0];
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
            {
                TriggerCompletionEvaluation();
            }
        }
    }

    private void OnTextAreaKeyDown(object? sender, KeyEventArgs e)
    {
        // Run query on Ctrl+Enter or Cmd+Enter
        if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            ExecuteSql();
            e.Handled = true;
            return;
        }

        // Explain query on Ctrl+E or Cmd+E
        if (e.Key == Key.E && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _ = RunExplainAsync(analyze: false);
            e.Handled = true;
            return;
        }

        // Format SQL on Ctrl+Shift+F or Cmd+Shift+F
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            FormatSqlInEditor();
            e.Handled = true;
            return;
        }

        // Ctrl+Space forces completion popup
        if (e.Key == Key.Space && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            TriggerCompletionEvaluation(force: true);
            e.Handled = true;
        }
    }

    public void FormatSqlInEditor()
    {
        if (TxtSqlEditor.SelectionLength > 0 && !string.IsNullOrWhiteSpace(TxtSqlEditor.SelectedText))
        {
            var formattedSelection = _formatterService.Format(TxtSqlEditor.SelectedText);
            TxtSqlEditor.SelectedText = formattedSelection;
            TxtExecutionStatus.Text = "✨ Seleção SQL formatada!";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
            return;
        }

        var fullText = TxtSqlEditor.Text;
        if (string.IsNullOrWhiteSpace(fullText)) return;

        var formatted = _formatterService.Format(fullText);
        TxtSqlEditor.Text = formatted;
        TxtExecutionStatus.Text = "✨ SQL formatado!";
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
    }

    public void TriggerCompletionEvaluation(bool force = false)
    {
        var text = TxtSqlEditor.Text ?? string.Empty;
        var caret = Math.Min(TxtSqlEditor.CaretOffset, text.Length);

        var context = _lexerService.AnalyzeContext(text, caret);

        if (force || context.CurrentWord.Length >= 1 || context.IsAfterDot)
        {
            var items = _completionService.GetCompletions(context, _cachedTables, _cachedColumns, _cachedSnippets);
            if (items.Count > 0)
            {
                if (_completionWindow != null)
                {
                    _completionWindow.Close();
                    _completionWindow = null;
                }

                _completionWindow = new AvaloniaEdit.CodeCompletion.CompletionWindow(TxtSqlEditor.TextArea);
                _completionWindow.StartOffset = caret - context.CurrentWord.Length;
                _completionWindow.Width = 340;
                _completionWindow.MaxHeight = 220;
                _completionWindow.CompletionList.Background = Avalonia.Media.Brush.Parse("#141d2b");

                var data = _completionWindow.CompletionList.CompletionData;
                foreach (var item in items)
                {
                    data.Add(new SqlCompletionData(item));
                }

                try
                {
                    _completionWindow.Show();
                    _completionWindow.Closed += (s, e) => _completionWindow = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Completion] Error showing window: {ex.Message}");
                }
                return;
            }
        }

        if (_completionWindow != null)
        {
            _completionWindow.Close();
            _completionWindow = null;
        }
    }

    public void CommitActiveCompletion()
    {
        // Handled natively by AvaloniaEdit CompletionWindow
    }

    public void ExecuteSql()
    {
        var sql = TxtSqlEditor.SelectedText;
        if (string.IsNullOrWhiteSpace(sql))
        {
            sql = TxtSqlEditor.Text;
        }
        if (string.IsNullOrWhiteSpace(sql)) return;

        if (_parameterService.HasParameters(sql))
        {
            PromptAndExecuteWithParameters(sql, isExplain: false);
            return;
        }

        ExecuteSqlInternal(sql);
    }

    private void PromptAndExecuteWithParameters(string sql, bool isExplain, bool analyze = false)
    {
        var parameters = _parameterService.ExtractParameters(sql);
        if (parameters.Count == 0)
        {
            if (isExplain)
                _ = RunExplainInternalAsync(sql, analyze);
            else
                ExecuteSqlInternal(sql);
            return;
        }

        var dialog = new QueryParametersDialog();
        dialog.Setup(parameters, _parameterService);

        dialog.CancelRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.ExecuteRequested += (paramValues) =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;

            var finalSql = _parameterService.SubstituteParameters(sql, paramValues);
            if (isExplain)
            {
                _ = RunExplainInternalAsync(finalSql, analyze);
            }
            else
            {
                ExecuteSqlInternal(finalSql, sql);
            }
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public void CancelQuery()
    {
        if (_queryCts != null && !_queryCts.IsCancellationRequested)
        {
            TxtExecutionStatus.Text = "🛑 Cancelando consulta...";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            BtnCancelQuery.IsEnabled = false;
            try
            {
                _queryCts.Cancel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QueryTab] Error canceling query: {ex.Message}");
            }
        }
    }

    public int? GetSelectedAutoLimit()
    {
        if (ComboAutoLimit?.SelectedItem is ComboBoxItem item)
        {
            var text = item.Content?.ToString()?.Trim() ?? string.Empty;
            return text switch
            {
                "100" => 100,
                "500" => 500,
                "1.000" => 1000,
                "5.000" => 5000,
                "Sem Limite" => null,
                _ => 1000
            };
        }
        return 1000;
    }

    public async void ExecuteSqlInternal(string sql, string? originalTemplateSql = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;

        var selectedLimit = GetSelectedAutoLimit();
        var executableSql = sql;
        bool autoLimitApplied = false;
        int appliedLimit = 0;

        if (selectedLimit.HasValue)
        {
            var (limitedSql, applied) = _autoLimitService.ApplyAutoLimit(sql, selectedLimit.Value);
            if (applied)
            {
                executableSql = limitedSql;
                autoLimitApplied = true;
                appliedLimit = selectedLimit.Value;
            }
        }

        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = new System.Threading.CancellationTokenSource();

        ProgressExecuting.IsVisible = true;
        BtnRun.IsVisible = false;
        BtnCancelQuery.IsVisible = true;
        BtnCancelQuery.IsEnabled = true;

        TxtExecutionStatus.Text = LocalizationManager.GetString("Query.Running", "Executing query...");
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#008bb9");
        BorderPlaceholder.IsVisible = false;

        QueryResult result;
        try
        {
            result = await _databaseService.ExecuteQueryAsync(executableSql, cancellationToken: _queryCts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new QueryResult
            {
                Success = false,
                ErrorMessage = "Consulta cancelada pelo usuário."
            };
        }
        catch (Exception ex)
        {
            result = new QueryResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            ProgressExecuting.IsVisible = false;
            BtnRun.IsVisible = true;
            BtnCancelQuery.IsVisible = false;
        }

        _lastResult = result;
        SwitchBottomTab(showExplain: false);

        var historyItem = new QueryHistoryItem
        {
            Sql = originalTemplateSql ?? sql,
            Success = result.Success,
            RowCount = result.Rows.Count,
            DurationMs = result.ElapsedMilliseconds
        };
        QueryExecuted?.Invoke(historyItem);

        // Persistent history log in AppStorage
        try
        {
            var connId = _databaseService.Config.Name ?? _databaseService.Config.Database;
            _ = _appStorage.LogQueryExecutionAsync(new QueryHistoryEntry
            {
                ConnectionId = connId,
                SqlText = originalTemplateSql ?? sql,
                ElapsedMilliseconds = (long)result.ElapsedMilliseconds,
                RowCount = result.Rows.Count,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QueryTab] Error logging history to app storage: {ex.Message}");
        }

        DisplayQueryResult(result, autoLimitApplied ? appliedLimit : null);
    }

    private void DisplayQueryResult(QueryResult result, int? autoLimit = null)
    {
        if (!result.Success)
        {
            BarSubResults.IsVisible = false;
            ContainerSplitGrids.IsVisible = false;
            GridResults.IsVisible = true;
            TxtExecutionStatus.Text = $"✗ {result.ErrorMessage}";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            GridResults.Columns.Clear();
            GridResults.ItemsSource = null;
            TxtRowStats.Text = result.ErrorMessage?.Contains("cancelada", StringComparison.OrdinalIgnoreCase) == true
                ? "Consulta cancelada"
                : LocalizationManager.GetString("Query.ErrorBanner", "Query execution failed");
            TxtTimeStats.Text = $"{result.ElapsedMilliseconds:F1} ms";
            return;
        }

        if (result.HasMultipleResults)
        {
            _activeSubResultIndex = 0;
            BarSubResults.IsVisible = true;
            BuildSubResultTabs(result.SubResults);
            BuildSplitGrids(result.SubResults);

            SetSubResultLayoutMode(_isSplitLayoutMode);
            SelectSubResult(0);
        }
        else
        {
            BarSubResults.IsVisible = false;
            ContainerSplitGrids.IsVisible = false;
            GridResults.IsVisible = true;

            PopulateDataGrid(GridResults, result);
            ViewChart.SetData(result);

            var successPattern = LocalizationManager.GetString("Query.RowsReturned", "{0} rows returned in {1}ms");
            TxtExecutionStatus.Text = $"✓ {string.Format(successPattern, result.Rows.Count, result.ElapsedMilliseconds.ToString("F1"))}";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");

            if (autoLimit.HasValue)
            {
                TxtRowStats.Text = $"{result.Rows.Count:N0} linhas (Auto-Limit: {autoLimit.Value:N0})";
            }
            else
            {
                TxtRowStats.Text = $"{result.Rows.Count:N0} rows";
            }
            TxtTimeStats.Text = $"{result.ElapsedMilliseconds:F1} ms";
        }
    }

    private void BuildSubResultTabs(IReadOnlyList<QueryResult> subResults)
    {
        PanelSubResultButtons.Children.Clear();

        for (int i = 0; i < subResults.Count; i++)
        {
            var idx = i;
            var subRes = subResults[i];
            var btn = new Button
            {
                Content = $"📊 #{idx + 1} ({subRes.Rows.Count:N0} lnh)",
                Classes = { idx == _activeSubResultIndex ? "btn-secondary" : "btn-outline" },
                Padding = new Avalonia.Thickness(10, 4),
                FontSize = 11,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            btn.Click += (s, e) => SelectSubResult(idx);
            PanelSubResultButtons.Children.Add(btn);
        }
    }

    private void BuildSplitGrids(IReadOnlyList<QueryResult> subResults)
    {
        PanelSplitGridsHost.Children.Clear();

        for (int i = 0; i < subResults.Count; i++)
        {
            var idx = i;
            var subRes = subResults[i];

            var card = new Border
            {
                Background = Avalonia.Media.Brush.Parse("#0c111a"),
                BorderBrush = Avalonia.Media.Brush.Parse("#1e2d42"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8)
            };

            var stack = new StackPanel { Spacing = 6 };

            // Card Header
            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var titleBlock = new TextBlock
            {
                Text = $"Resultado #{idx + 1} ({subRes.Rows.Count:N0} linhas em {subRes.ElapsedMilliseconds:F1}ms)",
                FontSize = 11.5,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Avalonia.Media.Brush.Parse("#38bdf8"),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            headerGrid.Children.Add(titleBlock);

            stack.Children.Add(headerGrid);

            // Grid for this result
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                BorderThickness = new Avalonia.Thickness(0),
                Background = Avalonia.Media.Brush.Parse("#090d14"),
                Foreground = Avalonia.Media.Brush.Parse("#f0f6fc"),
                RowBackground = Avalonia.Media.Brush.Parse("#0c111a"),
                MinHeight = 120,
                MaxHeight = 260
            };

            PopulateDataGrid(grid, subRes);

            grid.SelectionChanged += (s, e) =>
            {
                if (DrawerInspector.IsVisible && grid.SelectedItem is List<object?> selectedRow)
                {
                    DrawerInspector.LoadRow(subRes.Columns, selectedRow);
                }
            };

            stack.Children.Add(grid);
            card.Child = stack;
            PanelSplitGridsHost.Children.Add(card);
        }
    }

    private void SelectSubResult(int index)
    {
        if (_lastResult?.SubResults == null || index < 0 || index >= _lastResult.SubResults.Count)
            return;

        _activeSubResultIndex = index;

        // Update button visual states in tabs toolbar
        for (int i = 0; i < PanelSubResultButtons.Children.Count; i++)
        {
            if (PanelSubResultButtons.Children[i] is Button b)
            {
                if (i == index)
                {
                    b.Classes.Set("btn-secondary", true);
                    b.Classes.Set("btn-outline", false);
                }
                else
                {
                    b.Classes.Set("btn-secondary", false);
                    b.Classes.Set("btn-outline", true);
                }
            }
        }

        var active = _lastResult.SubResults[index];
        PopulateDataGrid(GridResults, active);
        ViewChart.SetData(active);

        TxtExecutionStatus.Text = $"✓ #{index + 1}: {active.Rows.Count:N0} linhas em {active.ElapsedMilliseconds:F1}ms (Total de {_lastResult.SubResults.Count} consultas)";
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");

        TxtRowStats.Text = $"{active.Rows.Count:N0} rows (Consulta #{index + 1})";
        TxtTimeStats.Text = $"{active.ElapsedMilliseconds:F1} ms";

        if (DrawerInspector.IsVisible)
        {
            SyncSelectedRowToInspector();
        }
    }

    private void SetSubResultLayoutMode(bool isSplit)
    {
        _isSplitLayoutMode = isSplit;

        if (isSplit)
        {
            GridResults.IsVisible = false;
            ContainerSplitGrids.IsVisible = true;
            BtnSubLayoutTabs.Classes.Set("btn-secondary", false);
            BtnSubLayoutTabs.Classes.Set("btn-outline", true);
            BtnSubLayoutSplit.Classes.Set("btn-secondary", true);
            BtnSubLayoutSplit.Classes.Set("btn-outline", false);
        }
        else
        {
            GridResults.IsVisible = true;
            ContainerSplitGrids.IsVisible = false;
            BtnSubLayoutTabs.Classes.Set("btn-secondary", true);
            BtnSubLayoutTabs.Classes.Set("btn-outline", false);
            BtnSubLayoutSplit.Classes.Set("btn-secondary", false);
            BtnSubLayoutSplit.Classes.Set("btn-outline", true);
        }
    }

    private void PopulateDataGrid(DataGrid grid, QueryResult res)
    {
        grid.Columns.Clear();
        for (int i = 0; i < res.Columns.Count; i++)
        {
            var colIndex = i;
            var colName = res.Columns[i];

            var col = new DataGridTextColumn
            {
                Header = colName,
                Binding = new Binding($"[{colIndex}]"),
                CanUserSort = true,
                CustomSortComparer = new Runix.Core.Models.SafeRowComparer(colIndex)
            };
            grid.Columns.Add(col);
        }

        grid.ItemsSource = res.Rows;

        if (ReferenceEquals(grid, GridResults))
        {
            _currentActiveResult = res;
            BuildQueryColumnFilters(res.Columns);
        }
    }

    public void OpenExportDialog()
    {
        var sql = TxtSqlEditor.Text ?? string.Empty;
        var dialog = new ExportDialog();
        dialog.SetupForQuery(sql, _lastResult, _databaseService);

        dialog.CancelRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };

        dialog.ExportCompleted += result =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
            TxtExecutionStatus.Text = $"✓ Exportadas {result.RowsExported:N0} linhas ({result.FileSizeFormatted}) para {Path.GetFileName(result.FilePath)}";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public void SwitchBottomTab(bool showExplain) => SwitchBottomTab(showExplain ? 2 : 0);

    public void SwitchBottomTab(int tabIndex)
    {
        PanelTabResults.IsVisible = tabIndex == 0;
        ViewChart.IsVisible = tabIndex == 1;
        ViewExplainPlan.IsVisible = tabIndex == 2;

        SetTabButtonActive(BtnTabResults, tabIndex == 0);
        SetTabButtonActive(BtnTabChart, tabIndex == 1);
        SetTabButtonActive(BtnTabExplain, tabIndex == 2);

        if (tabIndex == 1)
        {
            ViewChart.RefreshChart();
        }
    }

        private static void SetTabButtonActive(Button btn, bool isActive)
    {
        btn.Classes.Clear();
        btn.Classes.Add(isActive ? "btn-secondary" : "btn-outline");
    }

    public void ToggleInspectorDrawer()
    {
        DrawerInspector.IsVisible = !DrawerInspector.IsVisible;
        SetTabButtonActive(BtnToggleInspector, DrawerInspector.IsVisible);

        if (DrawerInspector.IsVisible)
        {
            if (GridResults.SelectedItem == null && _lastResult != null && _lastResult.Rows.Count > 0)
            {
                GridResults.SelectedIndex = 0;
            }
            SyncSelectedRowToInspector();
        }
    }

    private void SyncSelectedRowToInspector()
    {
        if (_lastResult != null && GridResults.SelectedItem is IReadOnlyList<object?> row)
        {
            DrawerInspector.LoadRow(_lastResult.Columns, row);
        }
        else
        {
            DrawerInspector.LoadRow(Array.Empty<string>(), Array.Empty<object?>());
        }
    }

    public async Task RunExplainAsync(bool analyze = false)
    {
        var sql = TxtSqlEditor.SelectedText;
        if (string.IsNullOrWhiteSpace(sql))
        {
            sql = TxtSqlEditor.Text;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            TxtExecutionStatus.Text = "Nenhuma query informada para o Explain.";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            return;
        }

        if (_parameterService.HasParameters(sql))
        {
            PromptAndExecuteWithParameters(sql, isExplain: true, analyze: analyze);
            return;
        }

        await RunExplainInternalAsync(sql, analyze);
    }

    private async Task RunExplainInternalAsync(string sql, bool analyze = false)
    {
        TxtExecutionStatus.Text = analyze ? "Executando Explain & Analyze..." : "Obtendo Explain estimado...";
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#38bdf8");
        ProgressExecuting.IsVisible = true;

        try
        {
            var plan = await SqlExplainService.Instance.ExplainQueryAsync(_databaseService, sql, analyze);
            ViewExplainPlan.LoadPlan(plan);
            SwitchBottomTab(showExplain: true);

            var timeText = plan.ExecutionTimeMs > 0 ? $"{plan.ExecutionTimeMs:F2} ms" : "estimado";
            TxtExecutionStatus.Text = $"✓ Explain concluído ({plan.Dialect}, {timeText}, custo: {plan.TotalCost:N1})";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
            TxtRowStats.Text = $"Explain: {plan.TotalNodeCount} nós ({plan.Dialect})";
        }
        catch (Exception ex)
        {
            TxtExecutionStatus.Text = $"Erro ao gerar Explain: {ex.Message}";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
        }
        finally
        {
            ProgressExecuting.IsVisible = false;
        }
    }

    public void ToggleQueryFiltersBar()
    {
        BarQueryColumnFilters.IsVisible = !BarQueryColumnFilters.IsVisible;
        BtnToggleQueryFilters.Classes.Clear();
        BtnToggleQueryFilters.Classes.Add(BarQueryColumnFilters.IsVisible ? "btn-secondary" : "btn-outline");

        if (BarQueryColumnFilters.IsVisible && _queryColumnFilterInputs.Count == 0 && _currentActiveResult != null && _currentActiveResult.Columns.Count > 0)
        {
            BuildQueryColumnFilters(_currentActiveResult.Columns);
            BarQueryColumnFilters.IsVisible = true;
            BtnToggleQueryFilters.Classes.Clear();
            BtnToggleQueryFilters.Classes.Add("btn-secondary");
        }
    }

    private void BuildQueryColumnFilters(List<string> columns)
    {
        if (columns.Count == 0)
        {
            BarQueryColumnFilters.IsVisible = false;
            BtnToggleQueryFilters.Classes.Clear();
            BtnToggleQueryFilters.Classes.Add("btn-outline");
            return;
        }

        // Ao executar um select, a barra permanece oculta por default
        BarQueryColumnFilters.IsVisible = false;
        BtnToggleQueryFilters.Classes.Clear();
        BtnToggleQueryFilters.Classes.Add("btn-outline");

        var isMatch = _queryColumnFilterInputs.Count == columns.Count && columns.All(c => _queryColumnFilterInputs.ContainsKey(c));
        if (isMatch) return;

        PanelQueryFiltersHost.Children.Clear();
        _queryColumnFilterInputs.Clear();

        foreach (var col in columns)
        {
            var panel = new StackPanel
            {
                Width = 145,
                Margin = new Avalonia.Thickness(0, 0, 8, 4)
            };

            var header = new TextBlock
            {
                Text = col,
                FontSize = 10.5,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#38bdf8")),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin = new Avalonia.Thickness(2, 0, 0, 4)
            };
            panel.Children.Add(header);

            var tb = new TextBox
            {
                Watermark = "ex: >10, %abc%",
                FontSize = 11,
                Padding = new Avalonia.Thickness(6, 4),
                Height = 28
            };

            tb.KeyDown += (s, ev) =>
            {
                if (ev.Key == Avalonia.Input.Key.Enter)
                {
                    ApplyQueryFilters();
                }
            };

            _queryColumnFilterInputs[col] = tb;
            panel.Children.Add(tb);
            PanelQueryFiltersHost.Children.Add(panel);
        }
    }

    private void ApplyQueryFilters()
    {
        if (_currentActiveResult == null) return;

        var activeFilters = new Dictionary<int, string>();
        for (int i = 0; i < _currentActiveResult.Columns.Count; i++)
        {
            var col = _currentActiveResult.Columns[i];
            if (_queryColumnFilterInputs.TryGetValue(col, out var tb) && !string.IsNullOrWhiteSpace(tb.Text))
            {
                activeFilters[i] = tb.Text.Trim();
            }
        }

        if (activeFilters.Count == 0)
        {
            ClearQueryFilters();
            return;
        }

        var filteredRows = new List<List<object?>>();
        foreach (var row in _currentActiveResult.Rows)
        {
            bool matchesAll = true;
            foreach (var (colIdx, expr) in activeFilters)
            {
                var val = (colIdx < row.Count) ? row[colIdx] : null;
                if (!_columnFilterService.MatchesValue(val, expr))
                {
                    matchesAll = false;
                    break;
                }
            }

            if (matchesAll)
            {
                filteredRows.Add(row);
            }
        }

        GridResults.ItemsSource = filteredRows;
        BadgeQueryActiveFilters.IsVisible = true;
        TxtQueryActiveFiltersCount.Text = $"{activeFilters.Count} ativo{(activeFilters.Count > 1 ? "s" : "")}";
        TxtRowStats.Text = $"{filteredRows.Count:N0} de {_currentActiveResult.Rows.Count:N0} linhas (filtrado)";
    }

    private void ClearQueryFilters()
    {
        foreach (var tb in _queryColumnFilterInputs.Values)
        {
            tb.Text = "";
        }

        BadgeQueryActiveFilters.IsVisible = false;
        if (_currentActiveResult != null)
        {
            GridResults.ItemsSource = _currentActiveResult.Rows;
            TxtRowStats.Text = $"{_currentActiveResult.Rows.Count:N0} rows";
        }
    }

    private void OnFilterBySelectedQueryCellValue()
    {
        if (GridResults.SelectedItem is List<object?> row && GridResults.CurrentColumn is { } currentCol)
        {
            var colIndex = GridResults.Columns.IndexOf(currentCol);
            if (colIndex >= 0 && _currentActiveResult != null && colIndex < _currentActiveResult.Columns.Count)
            {
                var colName = _currentActiveResult.Columns[colIndex];
                var val = (colIndex < row.Count) ? row[colIndex] : null;
                var strVal = val == null || val == DBNull.Value ? "IS NULL" : val.ToString() ?? "";

                if (!_queryColumnFilterInputs.ContainsKey(colName) && _currentActiveResult.Columns.Count > 0)
                {
                    BuildQueryColumnFilters(_currentActiveResult.Columns);
                }

                if (_queryColumnFilterInputs.TryGetValue(colName, out var tb))
                {
                    tb.Text = val == null || val == DBNull.Value ? "IS NULL" : strVal;
                    BarQueryColumnFilters.IsVisible = true;
                    ApplyQueryFilters();
                }
            }
        }
    }

    private async Task CopySelectedQueryCellValueAsync()
    {
        if (GridResults.SelectedItem is List<object?> row && GridResults.CurrentColumn is { } currentCol)
        {
            var colIndex = GridResults.Columns.IndexOf(currentCol);
            var val = (colIndex >= 0 && colIndex < row.Count) ? row[colIndex] : null;
            var text = val?.ToString() ?? "";
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }
}