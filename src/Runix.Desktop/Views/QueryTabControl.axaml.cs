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

    private List<TableInfo> _cachedTables = new();
    private Dictionary<string, IReadOnlyList<ColumnInfo>> _cachedColumns = new(StringComparer.OrdinalIgnoreCase);
    private List<CustomSnippet> _cachedSnippets = new();

    private AvaloniaEdit.CodeCompletion.CompletionWindow? _completionWindow;

    public int CaretIndex
    {
        get => TxtSqlEditor.CaretOffset;
        set => TxtSqlEditor.CaretOffset = Math.Max(0, Math.Min(value, TxtSqlEditor.Text?.Length ?? 0));
    }

    public QueryTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public QueryTabControl(IDatabaseService databaseService, string? initialSql = null, IAppStorageService? appStorage = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _appStorage = appStorage ?? AppStorageService.Instance;

        // Configure modern AvaloniaEdit SQL editor with syntax highlighting & dark theme
        SqlSyntaxHighlighting.ConfigureSqlEditor(TxtSqlEditor);

        if (!string.IsNullOrWhiteSpace(initialSql))
        {
            TxtSqlEditor.Text = initialSql;
        }

        BtnRun.Click += (s, e) => ExecuteSql();
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

    public async void ExecuteSql()
    {
        var sql = TxtSqlEditor.Text;
        if (string.IsNullOrWhiteSpace(sql)) return;

        ProgressExecuting.IsVisible = true;
        BtnRun.IsEnabled = false;

        TxtExecutionStatus.Text = LocalizationManager.GetString("Query.Running", "Executing query...");
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#008bb9");
        BorderPlaceholder.IsVisible = false;

        QueryResult result;
        try
        {
            result = await _databaseService.ExecuteQueryAsync(sql);
        }
        finally
        {
            ProgressExecuting.IsVisible = false;
            BtnRun.IsEnabled = true;
        }

        _lastResult = result;
        SwitchBottomTab(showExplain: false);

        var historyItem = new QueryHistoryItem
        {
            Sql = sql,
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
                SqlText = sql,
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

        if (!result.Success)
        {
            TxtExecutionStatus.Text = $"✗ {result.ErrorMessage}";
            TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#f87171");
            GridResults.Columns.Clear();
            GridResults.ItemsSource = null;
            TxtRowStats.Text = LocalizationManager.GetString("Query.ErrorBanner", "Query execution failed");
            TxtTimeStats.Text = $"{result.ElapsedMilliseconds:F1} ms";
            return;
        }

        var successPattern = LocalizationManager.GetString("Query.RowsReturned", "{0} rows returned in {1}ms");
        TxtExecutionStatus.Text = $"✓ {string.Format(successPattern, result.Rows.Count, result.ElapsedMilliseconds.ToString("F1"))}";
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");

        TxtRowStats.Text = $"{result.Rows.Count:N0} rows";
        TxtTimeStats.Text = $"{result.ElapsedMilliseconds:F1} ms";

        // Build dynamic columns
        GridResults.Columns.Clear();
        for (int i = 0; i < result.Columns.Count; i++)
        {
            var colIndex = i;
            var colName = result.Columns[i];

            var col = new DataGridTextColumn
            {
                Header = colName,
                Binding = new Binding($"[{colIndex}]"),
                CanUserSort = true
            };
            GridResults.Columns.Add(col);
        }

        GridResults.ItemsSource = result.Rows;
        ViewChart.SetData(result);
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
}