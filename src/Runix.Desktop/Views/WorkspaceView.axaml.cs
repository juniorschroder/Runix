using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Resilience;
using Runix.Core.Services;
using Runix.Desktop.Services;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private enum SidebarMode { Tables, Queries, History }

    private readonly IDatabaseService _databaseService;
    private readonly IProgrammabilityService _programmabilityService;
    private readonly IAppStorageService _appStorage;
    private readonly ICommandPaletteService _commandPaletteService = new CommandPaletteService();
    private readonly DdlGeneratorService _ddlGenerator = new();
    private List<TableInfo> _allTables = new();
    private List<DatabaseObjectInfo> _allProgrammableObjects = new();
    private List<SavedQuery> _allSavedQueries = new();
    private List<CustomSnippet> _allSnippets = new();
    private List<QueryHistoryEntry> _allHistory = new();
    private HashSet<string> _favoriteTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<DatabaseSidebarItem> _treeItems = new();
    private SidebarMode _currentMode = SidebarMode.Tables;
    private bool _filterOnlyFavorites = false;
    private int _queryCounter = 1;
    private readonly System.Timers.Timer _sessionAutoSaveTimer = new(1000) { AutoReset = false };
    private bool _isRestoringSession = false;

    public event Action? DisconnectRequested;
    public event Action? AboutRequested;
    public event Action? CheckUpdatesRequested;

    public WorkspaceView() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public WorkspaceView(IDatabaseService databaseService, IAppStorageService? appStorage = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _programmabilityService = new ProgrammabilityService(databaseService);
        _appStorage = appStorage ?? AppStorageService.Instance;

        _sessionAutoSaveTimer.Elapsed += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await SaveCurrentSessionAsync();
            });
        };

        SetupHeader();
        SetupEvents();
        UpdateLanguageButton();
        UpdateResponsiveHeaderLayout();

        _databaseService.ResilienceEventOccurred += OnResilienceEvent;

        // Sequentially initialize workspace: load schema catalog first, then restore session tabs
        _ = InitializeWorkspaceAsync();
    }

    private void SetupHeader()
    {
        TxtConnectionType.Text = $"[{_databaseService.Config.Type}]";
        TxtConnectionTarget.Text = _databaseService.Config.Type == DatabaseType.SQLite
            ? System.IO.Path.GetFileName(_databaseService.Config.FilePath)
            : $"{_databaseService.Config.Database} ({_databaseService.Config.Host})";

        BadgeWorkspaceProduction.IsVisible = _databaseService.Config.IsProductionEnvironment;
        BadgeWorkspaceReadOnly.IsVisible = _databaseService.Config.IsReadOnly;

        if (_databaseService.Config.Type != DatabaseType.PostgreSQL)
        {
            BtnOpenDbaTab.IsEnabled = false;
            ToolTip.SetTip(BtnOpenDbaTab, LocalizationManager.GetString("Dba.PostgresOnlyTooltip", "Administração de Usuários e Sessões disponível para conexões PostgreSQL."));
        }
    }

    private void UpdateLanguageButton()
    {
        TxtLanguageLabel.Text = LocalizationManager.CurrentLanguageLabel;
        ToolTip.SetTip(BtnLanguageToggle, $"Alternar Idioma ({LocalizationManager.CurrentLanguageLabel})");
        UpdateMenuHeaders();
    }

    private void UpdateMenuHeaders()
    {
        var aboutLabel = LocalizationManager.GetString("About.Button", "Sobre").Replace("ℹ ", "").Trim();
        MenuAbout.Header = $"ℹ {aboutLabel} RUNIX (v{AppConstants.AppVersion})";
    }

    private void UpdateResponsiveHeaderLayout()
    {
        // "Ações" dropdown remains always visible; icon bar only hides on ultra-narrow windows (< 760px)
        bool isUltraNarrow = Bounds.Width > 0 && Bounds.Width < 760;
        PanelActionButtons.IsVisible = !isUltraNarrow;
        BtnMoreActions.IsVisible = true;
    }

    private void OnResilienceEvent(ResilienceEvent ev)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (ev.Type)
            {
                case ResilienceEventType.RetryAttempt:
                    BadgeResilience.IsVisible = true;
                    BadgeResilience.Background = Brush.Parse("#2e1f0e");
                    BadgeResilience.BorderBrush = Brush.Parse("#f59e0b");
                    TxtResilienceStatus.Foreground = Brush.Parse("#fbbf24");
                    var retryPattern = LocalizationManager.GetString("Resilience.Retrying", "Retrying {0}/{1}");
                    TxtResilienceStatus.Text = string.Format(retryPattern, ev.Attempt, ev.MaxAttempts);
                    break;

                case ResilienceEventType.CircuitOpen:
                    BadgeResilience.IsVisible = true;
                    BadgeResilience.Background = Brush.Parse("#2e1419");
                    BadgeResilience.BorderBrush = Brush.Parse("#f87171");
                    TxtResilienceStatus.Foreground = Brush.Parse("#fca5a5");
                    TxtResilienceStatus.Text = LocalizationManager.GetString("Resilience.CircuitOpen", "Circuit Open");
                    break;

                case ResilienceEventType.CircuitHalfOpen:
                    BadgeResilience.IsVisible = true;
                    BadgeResilience.Background = Brush.Parse("#1e2338");
                    BadgeResilience.BorderBrush = Brush.Parse("#60a5fa");
                    TxtResilienceStatus.Foreground = Brush.Parse("#93c5fd");
                    TxtResilienceStatus.Text = LocalizationManager.GetString("Resilience.CircuitHalfOpen", "Half-Open");
                    break;

                case ResilienceEventType.CircuitClosed:
                    BadgeResilience.Background = Brush.Parse("#122a20");
                    BadgeResilience.BorderBrush = Brush.Parse("#34d399");
                    TxtResilienceStatus.Foreground = Brush.Parse("#6ee7b7");
                    TxtResilienceStatus.Text = LocalizationManager.GetString("Resilience.Healthy", "Healthy");

                    var timer = new System.Timers.Timer(2500) { AutoReset = false };
                    timer.Elapsed += (s, e) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => BadgeResilience.IsVisible = false);
                        timer.Dispose();
                    };
                    timer.Start();
                    break;
            }
        });
    }

    private void SetupEvents()
    {
        BtnLanguageToggle.Click += (s, e) =>
        {
            LocalizationManager.ToggleLanguage();
            UpdateLanguageButton();
            UpdateSidebarStats();
        };

        LocalizationManager.LanguageChanged += lang =>
        {
            UpdateLanguageButton();
            UpdateSidebarStats();
        };

        BtnOpenCommandPalette.Click += (s, e) => OpenCommandPalette();
        BtnNewQueryTab.Click += (s, e) => OpenNewQueryTab();
        BtnOpenQueryBuilderTab.Click += (s, e) => OpenVisualQueryBuilderTab();
        BtnOpenGlobalSearchTab.Click += (s, e) => OpenGlobalSearchTab();
        BtnOpenHealthTab.Click += (s, e) => OpenDatabaseHealthTab();
        BtnOpenErdTab.Click += (s, e) => OpenErdTab();
        BtnOpenDbaTab.Click += (s, e) => OpenDbaTab();
        BtnOpenCompareTab.Click += (s, e) => OpenCompareTab();
        BtnOpenBackupTab.Click += (s, e) => OpenBackupRestoreTab();
        BtnOpenMigrationsTab.Click += (s, e) => OpenMigrationsTab();
        BtnRefreshSchema.Click += (s, e) => LoadSchema();
        BtnAbout.Click += (s, e) => AboutRequested?.Invoke();
        BtnDisconnect.Click += async (s, e) =>
        {
            await SaveCurrentSessionAsync();
            DisposeAllTabs();
            await _databaseService.DisconnectAsync();
            DisconnectRequested?.Invoke();
        };

        WorkspaceTabs.SelectionChanged += (s, e) =>
        {
            if (!_isRestoringSession && ReferenceEquals(e.Source, WorkspaceTabs))
            {
                ScheduleSessionSave();
            }
        };

        // Overflow Menu Actions (accessible even when screen is narrow)
        MenuCommandPalette.Click += (s, e) => OpenCommandPalette();
        MenuNewQuery.Click += (s, e) => OpenNewQueryTab();
        MenuOpenQueryBuilder.Click += (s, e) => OpenVisualQueryBuilderTab();
        MenuOpenGlobalSearch.Click += (s, e) => OpenGlobalSearchTab();
        MenuOpenHealth.Click += (s, e) => OpenDatabaseHealthTab();
        MenuOpenErd.Click += (s, e) => OpenErdTab();
        MenuOpenDba.Click += (s, e) => OpenDbaTab();
        MenuOpenCompare.Click += (s, e) => OpenCompareTab();
        MenuCaptureSnapshotHeader.Click += (s, e) => OpenCaptureSnapshotDialog(_databaseService);
        MenuSnapshotCatalogHeader.Click += (s, e) => OpenSnapshotCatalogDialog();
        MenuOpenBackup.Click += (s, e) => OpenBackupRestoreTab();
        MenuOpenMigrations.Click += (s, e) => OpenMigrationsTab();
        MenuRefreshSchema.Click += (s, e) => LoadSchema();
        MenuLanguageToggle.Click += (s, e) =>
        {
            LocalizationManager.ToggleLanguage();
            UpdateLanguageButton();
            UpdateSidebarStats();
        };
        MenuAbout.Click += (s, e) => AboutRequested?.Invoke();
        MenuDisconnect.Click += async (s, e) =>
        {
            DisposeAllTabs();
            await _databaseService.DisconnectAsync();
            DisconnectRequested?.Invoke();
        };

        // Command Palette Overlay Events & Global Shortcut (Ctrl/Cmd+K or Ctrl/Cmd+P)
        OverlayCommandPalette.CloseRequested += () => OverlayCommandPalette.IsVisible = false;
        OverlayCommandPalette.ItemExecuted += (item, secondary) =>
        {
            OverlayCommandPalette.IsVisible = false;
            ExecuteCommandPaletteItem(item, secondary);
        };

        AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (s, e) =>
        {
            bool hasCmdOrCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) ||
                                e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);
            if (hasCmdOrCtrl && (e.Key == Avalonia.Input.Key.K || e.Key == Avalonia.Input.Key.P))
            {
                e.Handled = true;
                OpenCommandPalette();
            }
        }, RoutingStrategies.Tunnel);

        // Responsive Header Breakpoint
        SizeChanged += (s, e) => UpdateResponsiveHeaderLayout();

        // Sidebar Navigation
        BtnSidebarTables.Click += (s, e) => SwitchSidebar(SidebarMode.Tables);
        BtnSidebarQueries.Click += (s, e) => SwitchSidebar(SidebarMode.Queries);
        BtnSidebarHistory.Click += (s, e) => SwitchSidebar(SidebarMode.History);

        // Filters
        TxtTableFilter.TextChanged += (s, e) => FilterTables();
        TxtQueriesFilter.TextChanged += (s, e) => FilterSavedQueries();
        TxtHistoryFilter.TextChanged += (s, e) => FilterHistory();

        BtnFilterFavoritesToggle.Click += (s, e) =>
        {
            _filterOnlyFavorites = !_filterOnlyFavorites;
            BtnFilterFavoritesToggle.Background = _filterOnlyFavorites ? Brush.Parse("#eab308") : Brush.Parse("#141d2b");
            BtnFilterFavoritesToggle.Foreground = _filterOnlyFavorites ? Brushes.Black : Brushes.White;
            FilterSavedQueries();
        };

        BtnClearHistory.Click += async (s, e) =>
        {
            await _appStorage.ClearQueryHistoryAsync();
            await LoadHistoryAsync();
        };

        // TreeTables Interactions & Context Menu
        BtnNewProgObject.Click += (s, e) => OpenNewProgrammableObjectTab();

        TreeTables.DoubleTapped += async (s, e) =>
        {
            if (TreeTables.SelectedItem is TableSidebarItem tableItem)
            {
                OpenTableDataTab(tableItem.Table.Name);
            }
            else if (TreeTables.SelectedItem is ProgrammabilitySidebarItem progItem)
            {
                OpenObjectEditorTab(progItem.ObjectInfo, isNew: false);
            }
            else if (TreeTables.SelectedItem is ColumnSidebarItem colItem)
            {
                await CopyTextToClipboard(colItem.Column.Name);
            }
        };

        TreeTables.AddHandler(Button.ClickEvent, OnTreeTableButtonClicked);

        MenuOpenTable.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                OpenTableDataTab(table.Name);
            }
        };

        MenuQuickQuery.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                OpenQuickQuery(table.Name);
            }
        };

        MenuViewStructure.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                if (table.IsView)
                {
                    var viewObj = _allProgrammableObjects.FirstOrDefault(o => o.ObjectType == DatabaseObjectType.View && string.Equals(o.Name, table.Name, StringComparison.OrdinalIgnoreCase))
                                  ?? new DatabaseObjectInfo { Schema = table.Schema, Name = table.Name, ObjectType = DatabaseObjectType.View };
                    OpenObjectEditorTab(viewObj, isNew: false);
                }
                else
                {
                    OpenTableStructureTab(table.Name);
                }
            }
            else if (TreeTables.SelectedItem is ProgrammabilitySidebarItem progItem)
            {
                OpenObjectEditorTab(progItem.ObjectInfo, isNew: false);
            }
        };

        MenuEditProgObject.Click += (s, e) =>
        {
            if (TreeTables.SelectedItem is ProgrammabilitySidebarItem progItem)
            {
                OpenObjectEditorTab(progItem.ObjectInfo, isNew: false);
            }
            else if (GetSelectedTable() is TableInfo table && table.IsView)
            {
                var viewObj = _allProgrammableObjects.FirstOrDefault(o => o.ObjectType == DatabaseObjectType.View && string.Equals(o.Name, table.Name, StringComparison.OrdinalIgnoreCase))
                              ?? new DatabaseObjectInfo { Schema = table.Schema, Name = table.Name, ObjectType = DatabaseObjectType.View };
                OpenObjectEditorTab(viewObj, isNew: false);
            }
            else
            {
                OpenNewProgrammableObjectTab(GetSelectedTable()?.Name);
            }
        };

        MenuNewProgObject.Click += (s, e) =>
        {
            OpenNewProgrammableObjectTab(GetSelectedTable()?.Name);
        };

        MenuToggleFavorite.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                ToggleTableFavorite(table);
            }
        };

        MenuCopyTableName.Click += async (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                await CopyTextToClipboard(table.Name);
            }
        };

        MenuCopyDdl.Click += async (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                try
                {
                    var cols = await _databaseService.GetTableColumnsAsync(table.Name);
                    var node = new ErdTableNode { Table = table, Columns = cols };
                    var ddl = _ddlGenerator.GenerateCreateTableSql(node, _databaseService.Config.Type);
                    await CopyTextToClipboard(ddl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MenuCopyDdl] Error: {ex.Message}");
                }
            }
        };

        MenuCaptureSnapshot.Click += (s, e) => OpenCaptureSnapshotDialog(_databaseService);

        MenuImportTableData.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                OpenTableImportDialog(table.Name);
            }
        };

        MenuExportTableData.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                OpenTableExportDialog(table.Name);
            }
        };

        MenuGenerateMockData.Click += (s, e) =>
        {
            if (GetSelectedTable() is TableInfo table)
            {
                OpenTableMockDataDialog(table.Name);
            }
        };

        // Saved Query Selection
        ListSavedQueries.SelectionChanged += (s, e) =>
        {
            if (ListSavedQueries.SelectedItem is SavedQuery query)
            {
                OpenOrLoadQuery(query.SqlText, query.Title);
                ListSavedQueries.SelectedItem = null;
            }
        };

        // History Selection
        ListHistory.SelectionChanged += (s, e) =>
        {
            if (ListHistory.SelectedItem is QueryHistoryEntry historyItem)
            {
                OpenOrLoadQuery(historyItem.SqlText);
                ListHistory.SelectedItem = null;
            }
        };

        // Routed button clicks for Saved Queries (Delete & Toggle Favorite)
        ListSavedQueries.AddHandler(Button.ClickEvent, async (object? sender, RoutedEventArgs e) =>
        {
            if (e.Source is Button btn)
            {
                if (btn.Name == "BtnDeleteSavedQueryItem" && btn.DataContext is SavedQuery queryToDelete)
                {
                    await _appStorage.DeleteSavedQueryAsync(queryToDelete.Id);
                    await LoadSavedQueriesAsync();
                    e.Handled = true;
                }
                else if (btn.Name == "BtnToggleFavorite" && btn.DataContext is SavedQuery queryToToggle)
                {
                    queryToToggle.IsFavorite = !queryToToggle.IsFavorite;
                    await _appStorage.SaveQueryAsync(queryToToggle);
                    await LoadSavedQueriesAsync();
                    e.Handled = true;
                }
            }
        });
    }

    public void SwitchToTablesTab() => SwitchSidebar(SidebarMode.Tables);
    public void SwitchToQueriesTab() => SwitchSidebar(SidebarMode.Queries);
    public void SwitchToHistoryTab() => SwitchSidebar(SidebarMode.History);

    private async void SwitchSidebar(SidebarMode mode)
    {
        _currentMode = mode;

        var activeBg = Brush.Parse("#008bb9");
        var activeFg = Brushes.White;
        var activeBorder = Brush.Parse("#38bdf8");

        var inactiveBg = Brush.Parse("#141d2b");
        var inactiveFg = Brush.Parse("#8b9cb3");
        var inactiveBorder = Brush.Parse("#1e2d42");

        // Reset all buttons
        BtnSidebarTables.Background = mode == SidebarMode.Tables ? activeBg : inactiveBg;
        BtnSidebarTables.Foreground = mode == SidebarMode.Tables ? activeFg : inactiveFg;
        BtnSidebarTables.BorderBrush = mode == SidebarMode.Tables ? activeBorder : inactiveBorder;

        BtnSidebarQueries.Background = mode == SidebarMode.Queries ? activeBg : inactiveBg;
        BtnSidebarQueries.Foreground = mode == SidebarMode.Queries ? activeFg : inactiveFg;
        BtnSidebarQueries.BorderBrush = mode == SidebarMode.Queries ? activeBorder : inactiveBorder;

        BtnSidebarHistory.Background = mode == SidebarMode.History ? activeBg : inactiveBg;
        BtnSidebarHistory.Foreground = mode == SidebarMode.History ? activeFg : inactiveFg;
        BtnSidebarHistory.BorderBrush = mode == SidebarMode.History ? activeBorder : inactiveBorder;

        // Visibility
        BorderTableSearch.IsVisible = mode == SidebarMode.Tables;
        TreeTables.IsVisible = mode == SidebarMode.Tables;

        BorderQueriesSearch.IsVisible = mode == SidebarMode.Queries;
        ListSavedQueries.IsVisible = mode == SidebarMode.Queries;

        BorderHistorySearch.IsVisible = mode == SidebarMode.History;
        ListHistory.IsVisible = mode == SidebarMode.History;

        // Data loading
        if (mode == SidebarMode.Tables)
        {
            FilterTables();
        }
        else if (mode == SidebarMode.Queries)
        {
            await LoadSavedQueriesAsync();
        }
        else if (mode == SidebarMode.History)
        {
            await LoadHistoryAsync();
        }
    }

    public async Task LoadSchemaAsync()
    {
        try
        {
            _favoriteTables = FavoriteTablesStorage.GetFavorites(GetDatabaseIdentifier());
            _allTables = await _databaseService.GetTablesAsync();
            _allProgrammableObjects = await _programmabilityService.GetProgrammableObjectsAsync();
            try
            {
                var queries = await _appStorage.GetSavedQueriesAsync();
                _allSavedQueries = queries.ToList();
                var snippets = await _appStorage.GetCustomSnippetsAsync();
                _allSnippets = snippets.ToList();
            }
            catch
            {
                // Ignore local storage errors
            }

            FilterTables();
            RebuildCommandPaletteCatalog();
        }
        catch (Exception ex)
        {
            TxtTableCount.Text = $"Error: {ex.Message}";
        }
    }

    private async Task InitializeWorkspaceAsync()
    {
        await LoadSchemaAsync();
        await RestoreWorkspaceSessionOrOpenDefaultAsync();
    }

    private void LoadSchema()
    {
        _ = LoadSchemaAsync();
    }

    public async Task LoadSavedQueriesAsync()
    {
        try
        {
            var queries = await _appStorage.GetSavedQueriesAsync();
            _allSavedQueries = queries.ToList();
            FilterSavedQueries();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceView] Error loading queries: {ex.Message}");
        }
    }

    public async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _appStorage.GetQueryHistoryAsync(limit: 150);
            _allHistory = history.ToList();
            FilterHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceView] Error loading history: {ex.Message}");
        }
    }

    private void FilterTables()
    {
        var filter = TxtTableFilter.Text?.Trim().ToLowerInvariant() ?? "";
        var matchingTables = string.IsNullOrEmpty(filter)
            ? _allTables
            : _allTables.Where(t => t.Name.ToLowerInvariant().Contains(filter) || t.Schema.ToLowerInvariant().Contains(filter)).ToList();

        _treeItems.Clear();

        // 1. Group: ⭐ Favoritas (if any match)
        var favTables = matchingTables.Where(t => _favoriteTables.Contains(t.Name)).ToList();
        if (favTables.Count > 0)
        {
            var favGroup = new SchemaOrGroupSidebarItem("Favoritas", "⭐");
            foreach (var t in favTables)
            {
                var item = new TableSidebarItem(t, isFavorite: true) { LoadColumnsHandler = LoadTableColumnsAsync };
                if (!string.IsNullOrEmpty(filter)) item.IsExpanded = true;
                favGroup.Children.Add(item);
            }
            favGroup.NotifyCountChanged();
            _treeItems.Add(favGroup);
        }

        // 2. Group by Schema (PostgreSQL) or Tables vs Views
        var distinctSchemas = matchingTables.Select(t => t.Schema).Where(s => !string.IsNullOrEmpty(s) && s != "main").Distinct().ToList();

        if (distinctSchemas.Count > 1)
        {
            // Multi-schema (e.g. PostgreSQL)
            foreach (var schema in distinctSchemas.OrderBy(s => s))
            {
                var schemaTables = matchingTables.Where(t => string.Equals(t.Schema, schema, StringComparison.OrdinalIgnoreCase)).ToList();
                var schemaGroup = new SchemaOrGroupSidebarItem(schema, "📁");
                foreach (var t in schemaTables)
                {
                    var isFav = _favoriteTables.Contains(t.Name);
                    var item = new TableSidebarItem(t, isFavorite: isFav) { LoadColumnsHandler = LoadTableColumnsAsync };
                    if (!string.IsNullOrEmpty(filter)) item.IsExpanded = true;
                    schemaGroup.Children.Add(item);
                }
                schemaGroup.NotifyCountChanged();
                _treeItems.Add(schemaGroup);
            }
        }
        else
        {
            // Single schema (SQLite or default PostgreSQL public)
            var normalTables = matchingTables.Where(t => !t.IsView).ToList();
            if (normalTables.Count > 0)
            {
                var tablesGroup = new SchemaOrGroupSidebarItem("Tabelas", "📁");
                foreach (var t in normalTables)
                {
                    var isFav = _favoriteTables.Contains(t.Name);
                    var item = new TableSidebarItem(t, isFavorite: isFav) { LoadColumnsHandler = LoadTableColumnsAsync };
                    if (!string.IsNullOrEmpty(filter)) item.IsExpanded = true;
                    tablesGroup.Children.Add(item);
                }
                tablesGroup.NotifyCountChanged();
                _treeItems.Add(tablesGroup);
            }

            var views = matchingTables.Where(t => t.IsView).ToList();
            if (views.Count > 0)
            {
                var viewsGroup = new SchemaOrGroupSidebarItem("Views", "👁️");
                foreach (var v in views)
                {
                    var isFav = _favoriteTables.Contains(v.Name);
                    var item = new TableSidebarItem(v, isFavorite: isFav) { LoadColumnsHandler = LoadTableColumnsAsync };
                    if (!string.IsNullOrEmpty(filter)) item.IsExpanded = true;
                    viewsGroup.Children.Add(item);
                }
                viewsGroup.NotifyCountChanged();
                _treeItems.Add(viewsGroup);
            }
        }

        // 3. Programmable Object Groups (Triggers, Functions & Procedures, Indexes)
        var matchingProg = string.IsNullOrEmpty(filter)
            ? _allProgrammableObjects
            : _allProgrammableObjects.Where(o =>
                o.Name.ToLowerInvariant().Contains(filter) ||
                (o.TargetTableName != null && o.TargetTableName.ToLowerInvariant().Contains(filter))).ToList();

        var triggers = matchingProg.Where(o => o.ObjectType == DatabaseObjectType.Trigger).ToList();
        if (triggers.Count > 0)
        {
            var trgGroup = new SchemaOrGroupSidebarItem("Triggers", "⚡") { IsExpanded = !string.IsNullOrEmpty(filter) };
            foreach (var trg in triggers)
            {
                trgGroup.Children.Add(new ProgrammabilitySidebarItem(trg));
            }
            trgGroup.NotifyCountChanged();
            _treeItems.Add(trgGroup);
        }

        var routines = matchingProg.Where(o => o.ObjectType is DatabaseObjectType.Function or DatabaseObjectType.Procedure).ToList();
        if (routines.Count > 0)
        {
            var fnGroup = new SchemaOrGroupSidebarItem("Functions & Procedures", "ƒ") { IsExpanded = !string.IsNullOrEmpty(filter) };
            foreach (var fn in routines)
            {
                fnGroup.Children.Add(new ProgrammabilitySidebarItem(fn));
            }
            fnGroup.NotifyCountChanged();
            _treeItems.Add(fnGroup);
        }

        var indexes = matchingProg.Where(o => o.ObjectType == DatabaseObjectType.Index).ToList();
        if (indexes.Count > 0)
        {
            var idxGroup = new SchemaOrGroupSidebarItem("Índices", "📇") { IsExpanded = !string.IsNullOrEmpty(filter) };
            foreach (var idx in indexes)
            {
                idxGroup.Children.Add(new ProgrammabilitySidebarItem(idx));
            }
            idxGroup.NotifyCountChanged();
            _treeItems.Add(idxGroup);
        }

        TreeTables.ItemsSource = null;
        TreeTables.ItemsSource = _treeItems;

        UpdateSidebarStats();
    }

    private void OnTreeTableButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn)
        {
            if (btn.DataContext is TableSidebarItem tableItem)
            {
                if (btn.Name == "BtnTableFavorite")
                {
                    ToggleTableFavorite(tableItem.Table);
                    e.Handled = true;
                }
                else if (btn.Name == "BtnTableQuery")
                {
                    OpenQuickQuery(tableItem.Table.Name);
                    e.Handled = true;
                }
            }
            else if (btn.DataContext is ProgrammabilitySidebarItem progItem && btn.Name == "BtnEditProgObject")
            {
                OpenObjectEditorTab(progItem.ObjectInfo, isNew: false);
                e.Handled = true;
            }
        }
    }

    public void OpenNewProgrammableObjectTab(string? targetTableName = null)
    {
        var defaultTable = targetTableName ?? _allTables.FirstOrDefault(t => !t.IsView)?.Name ?? "table_name";
        var obj = new DatabaseObjectInfo
        {
            Schema = _databaseService.Config.Type == DatabaseType.PostgreSQL ? "public" : "main",
            Name = "new_view",
            ObjectType = DatabaseObjectType.View,
            TargetTableName = defaultTable
        };
        OpenObjectEditorTab(obj, isNew: true);
    }

    public void OpenObjectEditorTab(DatabaseObjectInfo obj, bool isNew = false)
    {
        var tabHeaderTitle = isNew ? $"Novo {obj.ObjectType}" : obj.Name;
        var tabIcon = isNew ? "✨" : obj.Icon;
        if (!isNew)
        {
            foreach (var item in WorkspaceTabs.Items)
            {
                if (item is TabItem existingTab && existingTab.Tag is string tag && tag == $"obj:{obj.SpecificId}")
                {
                    WorkspaceTabs.SelectedItem = existingTab;
                    return;
                }
            }
        }

        var editorControl = new ObjectEditorTabControl(_databaseService, obj, isNewObject: isNew);
        editorControl.ObjectsChanged += () => _ = LoadSchemaAsync();

        var tab = CreateTabItem(tabHeaderTitle, tabIcon, editorControl);
        tab.Tag = isNew ? $"obj:new:{Guid.NewGuid():N}" : $"obj:{obj.SpecificId}";
        WorkspaceTabs.Items.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
    }

    private string GetDatabaseIdentifier() =>
        string.IsNullOrWhiteSpace(_databaseService.Config.FilePath)
            ? $"{_databaseService.Config.Host}_{_databaseService.Config.Database}"
            : _databaseService.Config.FilePath;

    private TableInfo? GetSelectedTable()
    {
        if (TreeTables.SelectedItem is TableSidebarItem item) return item.Table;
        if (TreeTables.SelectedItem is ColumnSidebarItem col)
        {
            return _allTables.FirstOrDefault(t => string.Equals(t.Name, col.Column.TableName, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    private void ToggleTableFavorite(TableInfo table)
    {
        if (_favoriteTables.Contains(table.Name))
            _favoriteTables.Remove(table.Name);
        else
            _favoriteTables.Add(table.Name);

        FavoriteTablesStorage.SetFavorites(GetDatabaseIdentifier(), _favoriteTables);
        FilterTables();
    }

    public void OpenQuickQuery(string tableName)
    {
        OpenNewQueryTab();
        var queryTab = GetActiveQueryTabControl();
        if (queryTab != null)
        {
            var sql = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 100;";
            queryTab.TxtSqlEditor.Text = sql;
            queryTab.ExecuteSql();
        }
    }

    private async Task CopyTextToClipboard(string text)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard != null)
        {
            await top.Clipboard.SetTextAsync(text);
        }
    }

    private async Task LoadTableColumnsAsync(TableSidebarItem tableItem)
    {
        if (tableItem.ColumnsLoaded || tableItem.IsLoadingColumns) return;
        tableItem.IsLoadingColumns = true;
        try
        {
            var cols = await _databaseService.GetTableColumnsAsync(tableItem.Table.Name);
            tableItem.Children.Clear();
            foreach (var col in cols)
            {
                col.TableName = tableItem.Table.Name;
                tableItem.Children.Add(new ColumnSidebarItem(col));
            }
            tableItem.ColumnsLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadTableColumnsAsync] Error: {ex.Message}");
        }
        finally
        {
            tableItem.IsLoadingColumns = false;
        }
    }

    private void FilterSavedQueries()
    {
        var filter = TxtQueriesFilter.Text?.Trim().ToLowerInvariant() ?? "";
        var list = _allSavedQueries.AsEnumerable();

        if (_filterOnlyFavorites)
        {
            list = list.Where(q => q.IsFavorite);
        }

        if (!string.IsNullOrEmpty(filter))
        {
            list = list.Where(q =>
                q.Title.ToLowerInvariant().Contains(filter) ||
                (q.Description != null && q.Description.ToLowerInvariant().Contains(filter)) ||
                q.SqlText.ToLowerInvariant().Contains(filter) ||
                q.Tags.Any(t => t.ToLowerInvariant().Contains(filter)));
        }

        // Favorites first, then title
        var ordered = list.OrderByDescending(q => q.IsFavorite).ThenBy(q => q.Title).ToList();

        ListSavedQueries.ItemsSource = null;
        ListSavedQueries.ItemsSource = ordered;

        UpdateSidebarStats();
    }

    private void FilterHistory()
    {
        var filter = TxtHistoryFilter.Text?.Trim().ToLowerInvariant() ?? "";
        var list = _allHistory.AsEnumerable();

        if (!string.IsNullOrEmpty(filter))
        {
            list = list.Where(h =>
                h.SqlText.ToLowerInvariant().Contains(filter) ||
                (h.ErrorMessage != null && h.ErrorMessage.ToLowerInvariant().Contains(filter)));
        }

        var ordered = list.OrderByDescending(h => h.ExecutedAt).ToList();

        ListHistory.ItemsSource = null;
        ListHistory.ItemsSource = ordered;

        UpdateSidebarStats();
    }

    private void UpdateSidebarStats()
    {
        if (_currentMode == SidebarMode.Tables)
        {
            var filter = TxtTableFilter.Text?.Trim().ToLowerInvariant() ?? "";
            var count = string.IsNullOrEmpty(filter)
                ? _allTables.Count
                : _allTables.Count(t => t.Name.ToLowerInvariant().Contains(filter) || t.Schema.ToLowerInvariant().Contains(filter));
            var pattern = LocalizationManager.GetString("TableData.ShowingRows", "{0} tabelas encontradas");
            TxtTableCount.Text = string.Format(pattern, count);
        }
        else if (_currentMode == SidebarMode.Queries)
        {
            var favCount = _allSavedQueries.Count(q => q.IsFavorite);
            TxtTableCount.Text = $"{_allSavedQueries.Count} consultas ({favCount} ⭐)";
        }
        else if (_currentMode == SidebarMode.History)
        {
            var count = (ListHistory.ItemsSource as System.Collections.ICollection)?.Count ?? _allHistory.Count;
            TxtTableCount.Text = $"{count} execuções no histórico";
        }
    }

    public void OpenBackupRestoreTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "backup")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var backupControl = new Runix.Desktop.Views.Dialogs.BackupRestoreDialog(_databaseService);
        backupControl.DatabaseRestored += () => _ = LoadSchemaAsync();

        var tabItem = CreateTabItem("Backup & Restore", "💾", backupControl);
        tabItem.Tag = "backup";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenMigrationsTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "migrations")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var migrationsControl = new Runix.Desktop.Views.Migrations.MigrationsTabControl(_databaseService);

        var tabItem = CreateTabItem("Migrações", "🚀", migrationsControl);
        tabItem.Tag = "migrations";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenGlobalSearchTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "global-search")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var searchControl = new GlobalSearchTabControl(_databaseService);
        searchControl.OpenFilteredTableRequested += (tableName, filterClause) =>
        {
            OpenTableDataTab(tableName);
            var tblControl = FindTableDataTabControl(tableName);
            tblControl?.ApplyFilterClause(filterClause);
        };

        var tabItem = CreateTabItem("Busca Global", "🔎", searchControl);
        tabItem.Tag = "global-search";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenDatabaseHealthTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "health")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var healthControl = new DatabaseHealthTabControl(_databaseService);
        var tabItem = CreateTabItem("Saúde do Banco", "🩺", healthControl);
        tabItem.Tag = "health";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenCommandPalette(string initialQuery = "")
    {
        RebuildCommandPaletteCatalog();
        OverlayCommandPalette.BindService(_commandPaletteService);
        OverlayCommandPalette.IsVisible = true;
        OverlayCommandPalette.OpenAndFocus(initialQuery);
    }

    private void RebuildCommandPaletteCatalog()
    {
        var actions = new List<CommandPaletteItem>
        {
            new() { Id = "action:new-query", Title = "Nova Consulta SQL", Subtitle = "Abrir uma nova aba de editor SQL interativo", Category = CommandPaletteCategory.Action, Icon = "⚡", ShortcutHint = "Query", Payload = "new-query" },
            new() { Id = "action:query-builder", Title = "Construtor Visual de Consultas (Query Builder)", Subtitle = "Montar SELECT, JOINs por FK, WHERE e GROUP BY visualmente", Category = CommandPaletteCategory.Action, Icon = "🧩", ShortcutHint = "VQB", Payload = "query-builder" },
            new() { Id = "action:global-search", Title = "Busca Global de Dados no Banco", Subtitle = "Pesquisar um termo em todas as tabelas simultaneamente", Category = CommandPaletteCategory.Action, Icon = "🔎", ShortcutHint = "Search", Payload = "global-search" },
            new() { Id = "action:health", Title = "Dashboard de Saúde & Telemetria (VACUUM / ANALYZE)", Subtitle = "Inspecionar tamanho, Cache Hit Ratio, Dead Tuples, índices e integridade", Category = CommandPaletteCategory.Action, Icon = "🩺", ShortcutHint = "Health", Payload = "health" },
            new() { Id = "action:erd", Title = "Diagrama Entidade-Relacionamento (DER Visual)", Subtitle = "Abrir canvas interativo de modelagem de schema e migrações", Category = CommandPaletteCategory.Action, Icon = "📐", ShortcutHint = "ERD", Payload = "erd" },
            new() { Id = "action:compare", Title = "Comparador de Schemas (Database Diff & Sync)", Subtitle = "Comparar dois bancos ou snapshots JSON e gerar script de sincronização", Category = CommandPaletteCategory.Action, Icon = "⚖️", ShortcutHint = "Diff", Payload = "compare" },
            new() { Id = "action:capture-snapshot", Title = "Salvar Snapshot de Schema (.rschema)", Subtitle = "Capturar estrutura compactada com integridade SHA-256", Category = CommandPaletteCategory.Action, Icon = "📸", ShortcutHint = "Snapshot", Payload = "capture-snapshot" },
            new() { Id = "action:snapshot-catalog", Title = "Abrir Catálogo de Snapshots de Schema", Subtitle = "Explorar snapshots salvos em ~/.runix/snapshots/ e comparar", Category = CommandPaletteCategory.Action, Icon = "🗂️", ShortcutHint = "Catalog", Payload = "snapshot-catalog" },
            new() { Id = "action:backup", Title = "Gerenciador de Backup & Restore", Subtitle = "Exportar backup online .db, dump .sql/.sql.gz ou restaurar banco", Category = CommandPaletteCategory.Action, Icon = "💾", ShortcutHint = "Backup", Payload = "backup" },
            new() { Id = "action:migrations", Title = "Central de Migrações (Database Evolution)", Subtitle = "Executar migrações versionadas V*__*.sql, auditoria e diff export", Category = CommandPaletteCategory.Action, Icon = "🚀", ShortcutHint = "Migrações", Payload = "migrations" },
            new() { Id = "action:new-ddl", Title = "Novo Objeto Programável (View, Trigger, Function, Index)", Subtitle = "Criar objeto a partir de template DDL com transação segura", Category = CommandPaletteCategory.Action, Icon = "➕", ShortcutHint = "DDL", Payload = "new-ddl" },
            new() { Id = "action:refresh", Title = "Atualizar Catálogo de Schema", Subtitle = "Recarregar tabelas, views e objetos programáveis", Category = CommandPaletteCategory.Action, Icon = "🔄", ShortcutHint = "Refresh", Payload = "refresh" },
            new() { Id = "action:language", Title = "Alternar Idioma da Interface (PT-BR / EN-US)", Subtitle = "Mudar idioma dinamicamente", Category = CommandPaletteCategory.Action, Icon = "🌐", ShortcutHint = "i18n", Payload = "language" },
            new() { Id = "action:check-updates", Title = "Verificar Atualizações do RUNIX (Check for Updates)", Subtitle = "Consultar a última release oficial e instaladores no GitHub", Category = CommandPaletteCategory.Action, Icon = "⬆️", ShortcutHint = "Update", Payload = "check-updates" },
            new() { Id = "action:about", Title = $"Sobre o RUNIX Database Studio (v{AppConstants.AppVersion})", Subtitle = $"Ver informações da versão {AppConstants.AppVersion}, arquitetura e criador José Carlos Schröder Júnior", Category = CommandPaletteCategory.Action, Icon = "ℹ️", ShortcutHint = "About", Payload = "about" },
            new() { Id = "action:disconnect", Title = "Desconectar da Sessão Atual", Subtitle = "Fechar conexão e voltar para a tela inicial", Category = CommandPaletteCategory.Action, Icon = "🔌", ShortcutHint = "Disconnect", Payload = "disconnect" }
        };

        if (_databaseService.Config.Type == DatabaseType.PostgreSQL)
        {
            actions.Add(new CommandPaletteItem
            {
                Id = "action:dba",
                Title = "Administração PostgreSQL (Roles, Privilégios & Sessões)",
                Subtitle = "Gerenciar usuários, matriz GRANT/REVOKE e pg_stat_activity",
                Category = CommandPaletteCategory.Action,
                Icon = "🛡️",
                ShortcutHint = "DBA",
                Payload = "dba"
            });
        }

        _commandPaletteService.RebuildCatalog(
            actions,
            _allTables,
            _allProgrammableObjects,
            _allSavedQueries,
            _allSnippets,
            _favoriteTables);
    }

    private async void ExecuteCommandPaletteItem(CommandPaletteItem item, bool secondaryAction)
    {
        switch (item.Category)
        {
            case CommandPaletteCategory.Action when item.Payload is string actionKey:
                switch (actionKey)
                {
                    case "new-query": OpenNewQueryTab(); break;
                    case "query-builder": OpenVisualQueryBuilderTab(); break;
                    case "global-search": OpenGlobalSearchTab(); break;
                    case "health": OpenDatabaseHealthTab(); break;
                    case "erd": OpenErdTab(); break;
                    case "dba": OpenDbaTab(); break;
                    case "compare": OpenCompareTab(); break;
                    case "capture-snapshot": OpenCaptureSnapshotDialog(_databaseService); break;
                    case "snapshot-catalog": OpenSnapshotCatalogDialog(); break;
                    case "backup": OpenBackupRestoreTab(); break;
                    case "migrations": OpenMigrationsTab(); break;
                    case "new-ddl": OpenNewProgrammableObjectTab(); break;
                    case "refresh": await LoadSchemaAsync(); break;
                    case "language":
                        LocalizationManager.ToggleLanguage();
                        UpdateLanguageButton();
                        UpdateSidebarStats();
                        break;
                    case "about":
                        AboutRequested?.Invoke();
                        break;
                    case "check-updates":
                        CheckUpdatesRequested?.Invoke();
                        break;
                    case "disconnect":
                        DisposeAllTabs();
                        await _databaseService.DisconnectAsync();
                        DisconnectRequested?.Invoke();
                        break;
                }
                break;

            case CommandPaletteCategory.Table when item.Payload is TableInfo tbl:
                if (secondaryAction)
                {
                    OpenTableStructureTab(tbl.Name);
                }
                else
                {
                    OpenTableDataTab(tbl.Name);
                }
                break;

            case CommandPaletteCategory.View when item.Payload is TableInfo viewTbl:
                if (secondaryAction)
                {
                    var viewObj = _allProgrammableObjects.FirstOrDefault(o =>
                                      o.ObjectType == DatabaseObjectType.View &&
                                      string.Equals(o.Name, viewTbl.Name, StringComparison.OrdinalIgnoreCase))
                                  ?? new DatabaseObjectInfo { Schema = viewTbl.Schema, Name = viewTbl.Name, ObjectType = DatabaseObjectType.View };
                    OpenObjectEditorTab(viewObj, isNew: false);
                }
                else
                {
                    OpenTableDataTab(viewTbl.Name);
                }
                break;

            case CommandPaletteCategory.ProgrammableObject when item.Payload is DatabaseObjectInfo progObj:
                OpenObjectEditorTab(progObj, isNew: false);
                break;

            case CommandPaletteCategory.SavedQuery when item.Payload is SavedQuery sq:
                OpenOrLoadQuery(sq.SqlText, sq.Title);
                break;

            case CommandPaletteCategory.Snippet when item.Payload is CustomSnippet snip:
                OpenOrLoadQuery(snip.Body, snip.Prefix);
                break;
        }
    }

    public void OpenVisualQueryBuilderTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "vqb")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var vqbControl = new VisualQueryBuilderTabControl(_databaseService);
        vqbControl.OpenInQueryEditorRequested += sql => OpenOrLoadQuery(sql, "Visual Query");

        var tabItem = CreateTabItem("Query Builder", "🧩", vqbControl);
        tabItem.Tag = "vqb";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenErdTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "erd")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var erdControl = new ErdTabControl(_databaseService);
        erdControl.TableCreated += () => LoadSchema();

        var title = LocalizationManager.GetString("Erd.TabTitle", "ERD Diagram");
        var tabItem = CreateTabItem(title, "📐", erdControl);
        tabItem.Tag = "erd";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public void OpenDbaTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "dba")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var dbaControl = new Runix.Desktop.Views.Dba.PostgresAdminTabControl(_databaseService);
        var title = LocalizationManager.GetString("Dba.TabTitle", "Administração");
        var tabItem = CreateTabItem(title, "🛡", dbaControl);
        tabItem.Tag = "dba";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public Runix.Desktop.Views.Dba.PostgresAdminTabControl? GetActiveDbaTabControl()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "dba" && t.Content is Runix.Desktop.Views.Dba.PostgresAdminTabControl dba)
            {
                return dba;
            }
        }
        return null;
    }

    public void OpenCompareTab()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "compare")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var compareControl = new Runix.Desktop.Views.Diff.SchemaCompareTabControl(_databaseService);
        var title = LocalizationManager.GetString("Diff.TabTitle", "Comparador");
        var tabItem = CreateTabItem(title, "⚖", compareControl);
        tabItem.Tag = "compare";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;
    }

    public Runix.Desktop.Views.Diff.SchemaCompareTabControl? GetActiveCompareTabControl()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "compare" && t.Content is Runix.Desktop.Views.Diff.SchemaCompareTabControl compare)
            {
                return compare;
            }
        }
        return null;
    }

    public void OpenCaptureSnapshotDialog(IDatabaseService db)
    {
        var dialog = new Runix.Desktop.Views.Dialogs.CaptureSnapshotDialog(db);
        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };
        dialog.SnapshotCaptured += (path, envelope) =>
        {
            // Snapshot captured successfully
        };
        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public void OpenSnapshotCatalogDialog()
    {
        var dialog = new Runix.Desktop.Views.Dialogs.SnapshotCatalogDialog();
        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };
        dialog.NewSnapshotRequested += () =>
        {
            OpenCaptureSnapshotDialog(_databaseService);
        };
        dialog.CompareRequested += item =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
            OpenSchemaCompareWithSnapshot(item.FilePath);
        };
        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
    }

    public void OpenSchemaCompareWithSnapshot(string snapshotFilePath)
    {
        OpenCompareTab();
        var compare = GetActiveCompareTabControl();
        if (compare != null)
        {
            _ = compare.LoadSnapshotFileDirectAsync(snapshotFilePath);
        }
    }

    public void OpenOrLoadQuery(string sqlText, string? title = null)
    {
        var active = GetActiveQueryTabControl();
        if (active != null && string.IsNullOrWhiteSpace(active.GetSqlText()))
        {
            active.SetSqlText(sqlText);
        }
        else
        {
            OpenNewQueryTab(sqlText, title);
        }
    }

    public void OpenNewQueryTab(string? initialSql = null, string? title = null, int caretOffset = -1, double verticalScroll = 0, double horizontalScroll = 0)
    {
        var queryControl = new QueryTabControl(_databaseService, initialSql, _appStorage);
        queryControl.QueryExecuted += async item =>
        {
            if (_currentMode == SidebarMode.History)
            {
                await LoadHistoryAsync();
            }
        };

        queryControl.QuerySaved += async saved =>
        {
            await LoadSavedQueriesAsync();
        };

        queryControl.SqlTextChanged += ScheduleSessionSave;

        if (caretOffset >= 0 || verticalScroll > 0 || horizontalScroll > 0)
        {
            queryControl.RestoreEditorPosition(caretOffset, verticalScroll, horizontalScroll);
        }

        var tabTitle = title ?? $"Query {_queryCounter++}";
        var tabItem = CreateTabItem(tabTitle, "⚡", queryControl);
        tabItem.Tag = "query";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;

        ScheduleSessionSave();
    }

    public QueryTabControl? GetActiveQueryTabControl()
    {
        if (WorkspaceTabs.SelectedItem is TabItem t && t.Content is QueryTabControl q)
        {
            return q;
        }
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem tab && tab.Content is QueryTabControl query)
            {
                return query;
            }
        }
        return null;
    }

    public ErdTabControl? GetActiveErdTabControl()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == "erd" && t.Content is ErdTabControl erd)
            {
                return erd;
            }
        }
        return null;
    }

    public void OpenTableDataTab(string tableName, string? initialFilter = null)
    {
        // Check if tab already open
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == $"data:{tableName}")
            {
                WorkspaceTabs.SelectedItem = t;
                if (!string.IsNullOrWhiteSpace(initialFilter) && t.Content is TableDataTabControl ctrl)
                {
                    ctrl.SetFilter(initialFilter);
                }
                return;
            }
        }

        var tableDataControl = new TableDataTabControl(_databaseService, tableName, initialFilter);
        tableDataControl.ViewStructureRequested += tbl => OpenTableStructureTab(tbl);
        tableDataControl.NavigateToForeignKeyRequested += (tbl, filter) => OpenTableDataTab(tbl, filter);

        var tabItem = CreateTabItem(tableName, "🗄", tableDataControl);
        tabItem.Tag = $"data:{tableName}";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;

        ScheduleSessionSave();
    }

    public TableDataTabControl? FindTableDataTabControl(string tableName)
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == $"data:{tableName}" && t.Content is TableDataTabControl control)
            {
                return control;
            }
        }
        return null;
    }

    public void OpenTableImportDialog(string tableName, string? preloadedFile = null)
    {
        OpenTableDataTab(tableName);
        var tableTab = FindTableDataTabControl(tableName);
        tableTab?.OpenImportDialog(preloadedFile);
    }

    public void OpenTableExportDialog(string tableName)
    {
        OpenTableDataTab(tableName);
        var tableTab = FindTableDataTabControl(tableName);
        tableTab?.OpenExportDialog();
    }

    public void OpenTableMockDataDialog(string tableName)
    {
        OpenTableDataTab(tableName);
        var tableTab = FindTableDataTabControl(tableName);
        tableTab?.OpenMockDataGeneratorDialog();
    }

    public void OpenTableStructureTab(string tableName)
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem t && t.Tag is string tag && tag == $"struct:{tableName}")
            {
                WorkspaceTabs.SelectedItem = t;
                return;
            }
        }

        var structureControl = new TableStructureTabControl(_databaseService, tableName);
        var tabItem = CreateTabItem($"{tableName} (Structure)", "📐", structureControl);
        tabItem.Tag = $"struct:{tableName}";
        WorkspaceTabs.Items.Add(tabItem);
        WorkspaceTabs.SelectedItem = tabItem;

        ScheduleSessionSave();
    }

    private TabItem CreateTabItem(string headerText, string icon, Control content)
    {
        var tab = new TabItem();

        // Custom Header with Close Button
        var headerPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };

        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var textBlock = new TextBlock
        {
            Text = headerText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Padding = new Avalonia.Thickness(4, 1),
            CornerRadius = new Avalonia.CornerRadius(3),
            Background = Brushes.Transparent,
            Foreground = Brush.Parse("#8b9cb3"),
            BorderThickness = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(6, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        closeBtn.Click += (s, e) =>
        {
            CloseTabItem(tab);
        };

        headerPanel.Children.Add(iconBlock);
        headerPanel.Children.Add(textBlock);
        headerPanel.Children.Add(closeBtn);

        tab.Header = headerPanel;
        tab.Content = content;
        return tab;
    }

    private void CloseTabItem(TabItem tab)
    {
        if (tab.Content is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
        }
        tab.Content = null;
        tab.Header = null;
        WorkspaceTabs.Items.Remove(tab);
        if (WorkspaceTabs.Items.Count == 0)
        {
            OpenNewQueryTab();
        }
        ScheduleSessionSave();
        MemoryTrimService.TrimIdleMemory();
    }

    private void DisposeAllTabs()
    {
        foreach (var item in WorkspaceTabs.Items)
        {
            if (item is TabItem tab)
            {
                if (tab.Content is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { }
                }
                tab.Content = null;
                tab.Header = null;
            }
        }
        WorkspaceTabs.Items.Clear();
        MemoryTrimService.TrimIdleMemory(forceLoh: true);
    }

    // ==========================================
    // PERSISTÊNCIA DE SESSÃO DO WORKSPACE (v1.2.0)
    // ==========================================

    public void ScheduleSessionSave()
    {
        if (_isRestoringSession) return;
        _sessionAutoSaveTimer.Stop();
        _sessionAutoSaveTimer.Start();
    }

    public async Task SaveCurrentSessionAsync()
    {
        if (_isRestoringSession) return;
        try
        {
            var session = CaptureSessionState();
            await _appStorage.SaveWorkspaceSessionAsync(session);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceSession] Auto-save error: {ex.Message}");
        }
    }

    public void SaveCurrentSessionSync()
    {
        try
        {
            var session = CaptureSessionState();
            _appStorage.SaveWorkspaceSessionAsync(session).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceSession] Sync save error: {ex.Message}");
        }
    }

    public WorkspaceSessionState CaptureSessionState()
    {
        var session = new WorkspaceSessionState
        {
            ConnectionKey = _databaseService.Config.GetSessionKey(),
            ActiveTabIndex = WorkspaceTabs.SelectedIndex >= 0 ? WorkspaceTabs.SelectedIndex : 0,
            LastSavedAt = DateTime.UtcNow
        };

        for (int i = 0; i < WorkspaceTabs.Items.Count; i++)
        {
            if (WorkspaceTabs.Items[i] is not TabItem tab) continue;

            string title = "Tab";
            if (tab.Header is StackPanel sp && sp.Children.Count >= 2 && sp.Children[1] is TextBlock tb)
            {
                title = tb.Text ?? "Tab";
            }

            var tabSession = new WorkspaceTabSession
            {
                Title = title,
                Tag = tab.Tag?.ToString()
            };

            if (tab.Content is QueryTabControl query)
            {
                tabSession.TabType = WorkspaceTabType.Query;
                tabSession.SqlText = query.GetSqlText();
                tabSession.CaretOffset = query.CaretIndex;
                tabSession.VerticalScrollOffset = query.VerticalScrollOffset;
                tabSession.HorizontalScrollOffset = query.HorizontalScrollOffset;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is TableDataTabControl data)
            {
                tabSession.TabType = WorkspaceTabType.TableData;
                tabSession.TableName = data.TableName;
                tabSession.FilterText = data.GetFilter();
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is TableStructureTabControl structure)
            {
                tabSession.TabType = WorkspaceTabType.TableStructure;
                tabSession.TableName = structure.TableName;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is ErdTabControl)
            {
                tabSession.TabType = WorkspaceTabType.Erd;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is VisualQueryBuilderTabControl)
            {
                tabSession.TabType = WorkspaceTabType.QueryBuilder;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is GlobalSearchTabControl)
            {
                tabSession.TabType = WorkspaceTabType.GlobalSearch;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is DatabaseHealthTabControl)
            {
                tabSession.TabType = WorkspaceTabType.Health;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is Runix.Desktop.Views.Dialogs.BackupRestoreDialog)
            {
                tabSession.TabType = WorkspaceTabType.Backup;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is Runix.Desktop.Views.Dba.PostgresAdminTabControl)
            {
                tabSession.TabType = WorkspaceTabType.Dba;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is Runix.Desktop.Views.Diff.SchemaCompareTabControl)
            {
                tabSession.TabType = WorkspaceTabType.Diff;
                session.Tabs.Add(tabSession);
            }
            else if (tab.Content is Runix.Desktop.Views.Migrations.MigrationsTabControl)
            {
                tabSession.TabType = WorkspaceTabType.Migrations;
                session.Tabs.Add(tabSession);
            }
        }

        return session;
    }

    private async Task RestoreWorkspaceSessionOrOpenDefaultAsync()
    {
        _isRestoringSession = true;
        try
        {
            var key = _databaseService.Config.GetSessionKey();
            var session = await _appStorage.GetWorkspaceSessionAsync(key);
            if (session != null && session.Tabs.Count > 0)
            {
                RestoreSession(session);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkspaceSession] Restore error: {ex.Message}");
        }
        finally
        {
            _isRestoringSession = false;
        }

        // Fallback default
        if (WorkspaceTabs.Items.Count == 0)
        {
            OpenNewQueryTab();
        }
    }

    public void RestoreSession(WorkspaceSessionState session)
    {
        _isRestoringSession = true;
        try
        {
            DisposeAllTabs();

            foreach (var tabState in session.Tabs)
            {
                if (tabState.Title.StartsWith("Query ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(tabState.Title.AsSpan(6).Trim(), out int qNum) &&
                    qNum >= _queryCounter)
                {
                    _queryCounter = qNum + 1;
                }

                switch (tabState.TabType)
                {
                    case WorkspaceTabType.Query:
                        OpenNewQueryTab(
                            initialSql: tabState.SqlText,
                            title: tabState.Title,
                            caretOffset: tabState.CaretOffset,
                            verticalScroll: tabState.VerticalScrollOffset,
                            horizontalScroll: tabState.HorizontalScrollOffset);
                        break;

                    case WorkspaceTabType.TableData:
                        if (!string.IsNullOrWhiteSpace(tabState.TableName))
                        {
                            OpenTableDataTab(tabState.TableName);
                            var control = FindTableDataTabControl(tabState.TableName);
                            if (control != null && !string.IsNullOrWhiteSpace(tabState.FilterText))
                            {
                                control.SetFilter(tabState.FilterText);
                            }
                        }
                        break;

                    case WorkspaceTabType.TableStructure:
                        if (!string.IsNullOrWhiteSpace(tabState.TableName))
                        {
                            OpenTableStructureTab(tabState.TableName);
                        }
                        break;

                    case WorkspaceTabType.Erd:
                        OpenErdTab();
                        break;

                    case WorkspaceTabType.QueryBuilder:
                        OpenVisualQueryBuilderTab();
                        break;

                    case WorkspaceTabType.GlobalSearch:
                        OpenGlobalSearchTab();
                        break;

                    case WorkspaceTabType.Health:
                        OpenDatabaseHealthTab();
                        break;

                    case WorkspaceTabType.Backup:
                        OpenBackupRestoreTab();
                        break;

                    case WorkspaceTabType.Dba:
                        OpenDbaTab();
                        break;

                    case WorkspaceTabType.Diff:
                        OpenCompareTab();
                        break;

                    case WorkspaceTabType.Migrations:
                        OpenMigrationsTab();
                        break;
                }
            }

            if (WorkspaceTabs.Items.Count == 0)
            {
                OpenNewQueryTab();
            }
            else
            {
                var targetIdx = Math.Clamp(session.ActiveTabIndex, 0, WorkspaceTabs.Items.Count - 1);
                WorkspaceTabs.SelectedIndex = targetIdx;
            }
        }
        finally
        {
            _isRestoringSession = false;
        }
    }
}