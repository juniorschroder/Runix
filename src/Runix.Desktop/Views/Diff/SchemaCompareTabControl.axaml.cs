using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.Diff;
using Runix.Desktop.Views.Dialogs;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Diff;
using Runix.Infrastructure.Diff.Exceptions;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Diff;

public class CompareEndpointItem
{
    public CompareEndpoint Endpoint { get; }
    public string Label => Endpoint.DisplayName;

    public CompareEndpointItem(CompareEndpoint endpoint)
    {
        Endpoint = endpoint;
    }

    public override string ToString() => Label;
}

public partial class SchemaCompareTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _sourceDb;
    private readonly ISchemaDiffService _diffService;

    private SchemaSnapshotModel? _sourceSnapshot;
    private SchemaSnapshotModel? _targetSnapshot;
    private SchemaDiffResult? _diffResult;
    private List<TableDiffItem> _allTableDiffs = new();
    private IDatabaseService? _connectedTargetDb;
    private IDatabaseService? _connectedSourceDb;
    private string? _pendingIntegrityFilePath;
    private bool _pendingIsSourceSnapshot;

    public SchemaCompareTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public SchemaCompareTabControl(IDatabaseService sourceDb)
    {
        _sourceDb = sourceDb;
        _diffService = new SchemaDiffService();

        InitializeComponent();

        PopulateEndpointCombos();
        SetupEvents();
        UpdateStagingCount();
    }

    private void PopulateEndpointCombos()
    {
        CboSource.Items.Clear();
        CboTarget.Items.Clear();

        // 1. Active connection endpoint
        var dbName = _sourceDb.Config.Type == DatabaseType.SQLite
            ? Path.GetFileName(_sourceDb.Config.FilePath ?? "sqlite.db")
            : $"{_sourceDb.Config.Database} ({_sourceDb.Config.Host})";

        var activeEndpoint = new CompareEndpoint
        {
            Type = CompareEndpointType.ActiveConnection,
            DisplayName = $"[Ativa] {_sourceDb.Config.Type}: {dbName}",
            Dialect = _sourceDb.Config.Type
        };

        var activeItemSource = new CompareEndpointItem(activeEndpoint);
        var activeItemTarget = new CompareEndpointItem(activeEndpoint);

        CboSource.Items.Add(activeItemSource);
        CboTarget.Items.Add(activeItemTarget);

        // 2. Saved connections
        var savedConns = ConnectionStorageService.LoadConnections();
        foreach (var conn in savedConns)
        {
            var ep = new CompareEndpoint
            {
                Type = CompareEndpointType.SavedConnection,
                DisplayName = $"{conn.EngineIcon} {conn.Name} [{conn.Type}]",
                SavedConnection = conn,
                Dialect = conn.Type
            };
            CboSource.Items.Add(new CompareEndpointItem(ep));
            CboTarget.Items.Add(new CompareEndpointItem(ep));
        }

        // 3. Local snapshot catalog items
        try
        {
            var catalogItems = SchemaSnapshotCatalogService.Instance.GetAllAsync().GetAwaiter().GetResult();
            foreach (var item in catalogItems)
            {
                var ep = new CompareEndpoint
                {
                    Type = CompareEndpointType.LocalCatalogSnapshot,
                    DisplayName = $"📄 [Snapshot] {item.FileName} ({item.DatabaseName})",
                    FilePath = item.FilePath,
                    CatalogItem = item,
                    Dialect = item.EngineType == "SQLite" ? DatabaseType.SQLite : DatabaseType.PostgreSQL
                };
                CboSource.Items.Add(new CompareEndpointItem(ep));
                CboTarget.Items.Add(new CompareEndpointItem(ep));
            }
        }
        catch
        {
            // Catalog directory empty or not accessible yet
        }

        // Set defaults
        CboSource.SelectedItem = activeItemSource;
        if (CboTarget.Items.Count > 1)
        {
            CboTarget.SelectedIndex = 1;
        }
        else
        {
            CboTarget.SelectedIndex = 0;
        }
    }

    private void SetupEvents()
    {
        BtnSaveSnapshot.Click += (s, e) => OpenCaptureSnapshotDialog();
        BtnBrowseSourceSnapshot.Click += async (s, e) => await BrowseSnapshotAsync(isSource: true);
        BtnBrowseTargetSnapshot.Click += async (s, e) => await BrowseSnapshotAsync(isSource: false);
        BtnSwapEndpoints.Click += (s, e) => SwapEndpoints();
        BtnCompare.Click += async (s, e) => await ExecuteComparisonAsync();

        BtnSelectAll.Click += (s, e) => SetSelectionForAll(true);
        BtnDeselectAll.Click += (s, e) => SetSelectionForAll(false);
        BtnSelectAdditions.Click += (s, e) => SelectAdditionsOnly();

        TxtFilterTable.TextChanged += (s, e) => FilterTableList();
        GridTableDiffs.SelectionChanged += OnTableSelectionChanged;

        BtnGenerateSync.Click += (s, e) => OpenSyncPreview();
        BtnDismissCompromisedWarning.Click += (s, e) => PanelCompromisedWarning.IsVisible = false;

        ModalSyncPreview.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalSyncPreview.IsVisible = false;
        };
        ModalSyncPreview.ExecuteRequested += async () => await ExecuteSyncOnTargetAsync();
        ModalSyncPreview.SaveSnapshotRequested += async () => await SaveUpdatedSnapshotAsync();

        BtnCancelIntegrityModal.Click += (s, e) =>
        {
            PanelIntegrityModal.IsVisible = false;
            PanelModalOverlay.IsVisible = false;
            _pendingIntegrityFilePath = null;
        };

        BtnForceOpenSnapshot.Click += async (s, e) =>
        {
            PanelIntegrityModal.IsVisible = false;
            PanelModalOverlay.IsVisible = false;
            if (!string.IsNullOrEmpty(_pendingIntegrityFilePath))
            {
                var path = _pendingIntegrityFilePath;
                var isSrc = _pendingIsSourceSnapshot;
                _pendingIntegrityFilePath = null;
                await LoadSnapshotCoreAsync(path, isSrc, allowCompromised: true);
            }
        };
    }

    private void SwapEndpoints()
    {
        var srcIdx = CboSource.SelectedIndex;
        var tgtIdx = CboTarget.SelectedIndex;

        var tempSnapshot = _sourceSnapshot;
        _sourceSnapshot = _targetSnapshot;
        _targetSnapshot = tempSnapshot;

        var tempDb = _connectedSourceDb;
        _connectedSourceDb = _connectedTargetDb;
        _connectedTargetDb = tempDb;

        CboSource.SelectedIndex = tgtIdx;
        CboTarget.SelectedIndex = srcIdx;

        TxtStatusMessage.Text = "Origem e Destino invertidos.";

        if (_sourceSnapshot != null && _targetSnapshot != null)
        {
            RunDiffAndDisplay();
        }
    }

    private async Task BrowseSnapshotAsync(bool isSource)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Selecionar Snapshot para {(isSource ? "Origem" : "Destino")} (.rschema / .json)",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("RUNIX Schema Snapshot (*.rschema)") { Patterns = new[] { "*.rschema" } },
                new("Schema Snapshot JSON (*.json)") { Patterns = new[] { "*.json" } },
                new("Todos os arquivos (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            await LoadSnapshotCoreAsync(files[0].Path.LocalPath, isSource, allowCompromised: false);
        }
    }

    private async Task LoadSnapshotCoreAsync(string filePath, bool isSource, bool allowCompromised)
    {
        TxtStatusMessage.Text = "Carregando snapshot de schema...";

        try
        {
            var envelope = await SchemaSnapshotStorageService.Instance.LoadSnapshotAsync(filePath, allowCompromised);
            var snapshot = envelope.Snapshot;

            if (snapshot.IsIntegrityCompromised)
            {
                PanelCompromisedWarning.IsVisible = true;
            }
            else
            {
                PanelCompromisedWarning.IsVisible = false;
            }

            var fileName = Path.GetFileName(filePath);
            var label = !string.IsNullOrEmpty(snapshot.Label) ? $" ({snapshot.Label})" : "";
            var ep = new CompareEndpoint
            {
                Type = CompareEndpointType.SnapshotFile,
                DisplayName = $"📄 [Snapshot] {fileName}{label}",
                FilePath = filePath,
                Snapshot = snapshot,
                Dialect = snapshot.EngineType == "SQLite" ? DatabaseType.SQLite : DatabaseType.PostgreSQL
            };

            var item = new CompareEndpointItem(ep);

            if (isSource)
            {
                _sourceSnapshot = snapshot;
                CboSource.Items.Insert(0, item);
                CboSource.SelectedItem = item;
            }
            else
            {
                _targetSnapshot = snapshot;
                CboTarget.Items.Insert(0, item);
                CboTarget.SelectedItem = item;
            }

            TxtStatusMessage.Text = $"Snapshot carregado de {fileName} ({snapshot.Tables.Count} tabelas).";
        }
        catch (SnapshotIntegrityException ex)
        {
            TxtStatusMessage.Text = $"Aviso de integridade: {ex.Message}";
            ShowIntegrityErrorModal(filePath, isSource, ex.Message);
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Erro ao carregar snapshot: {ex.Message}";
        }
    }

    private void ShowIntegrityErrorModal(string filePath, bool isSource, string errorDetails)
    {
        _pendingIntegrityFilePath = filePath;
        _pendingIsSourceSnapshot = isSource;
        TxtIntegrityErrorDetails.Text = errorDetails;
        ModalSyncPreview.IsVisible = false;
        ModalContainer.IsVisible = false;
        PanelIntegrityModal.IsVisible = true;
        PanelModalOverlay.IsVisible = true;
    }

    public async Task ExecuteComparisonAsync()
    {
        TxtStatusMessage.Text = "Inspecionando endpoints e executando diff...";
        PanelEmptyDiff.IsVisible = false;

        try
        {
            // 1. Resolve source snapshot
            if (CboSource.SelectedItem is CompareEndpointItem srcItem)
            {
                _sourceSnapshot = await ResolveSnapshotFromEndpointAsync(srcItem.Endpoint, isSource: true);
            }

            // 2. Resolve target snapshot
            if (CboTarget.SelectedItem is CompareEndpointItem tgtItem)
            {
                _targetSnapshot = await ResolveSnapshotFromEndpointAsync(tgtItem.Endpoint, isSource: false);
            }

            if (_sourceSnapshot == null || _targetSnapshot == null)
            {
                TxtStatusMessage.Text = "Selecione a Origem e o Destino para comparar.";
                return;
            }

            RunDiffAndDisplay();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Falha ao comparar schemas: {ex.Message}";
        }
    }

    private async Task<SchemaSnapshotModel?> ResolveSnapshotFromEndpointAsync(CompareEndpoint endpoint, bool isSource)
    {
        if (endpoint.Snapshot != null)
        {
            return endpoint.Snapshot;
        }

        switch (endpoint.Type)
        {
            case CompareEndpointType.ActiveConnection:
                return await _diffService.ExtractSnapshotAsync(_sourceDb);

            case CompareEndpointType.SavedConnection:
                if (endpoint.SavedConnection == null) return null;
                var db = endpoint.SavedConnection.Type == DatabaseType.SQLite
                    ? (IDatabaseService)new SqliteDatabaseService(endpoint.SavedConnection)
                    : new PostgresDatabaseService(endpoint.SavedConnection);

                if (isSource)
                {
                    _connectedSourceDb?.Dispose();
                    _connectedSourceDb = db;
                }
                else
                {
                    _connectedTargetDb?.Dispose();
                    _connectedTargetDb = db;
                }

                await db.ConnectAsync();
                var extracted = await _diffService.ExtractSnapshotAsync(db);
                return extracted;

            case CompareEndpointType.LocalCatalogSnapshot:
            case CompareEndpointType.SnapshotFile:
                if (string.IsNullOrEmpty(endpoint.FilePath)) return null;
                var env = await SchemaSnapshotStorageService.Instance.LoadSnapshotAsync(endpoint.FilePath, allowCompromised: true);
                return env.Snapshot;

            default:
                return null;
        }
    }

    private void RunDiffAndDisplay()
    {
        if (_sourceSnapshot == null || _targetSnapshot == null) return;

        var options = new SchemaCompareOptions
        {
            StrictTypeMatching = ChkStrictTypes.IsChecked == true,
            InvertDirection = false
        };

        _diffResult = _diffService.CompareSnapshots(_sourceSnapshot, _targetSnapshot, options);
        _allTableDiffs = _diffResult.Tables;

        TxtSummaryTotal.Text = $"Total: {_allTableDiffs.Count}";
        TxtSummaryAdded.Text = $"+ {_diffResult.AddedCount} Novos";
        TxtSummaryModified.Text = $"~ {_diffResult.ModifiedCount} Modificados";
        TxtSummaryRemoved.Text = $"- {_diffResult.RemovedCount} Extras";

        FilterTableList();

        if (_allTableDiffs.Count > 0)
        {
            PanelEmptyDiff.IsVisible = false;
            GridTableDiffs.SelectedIndex = 0;
        }
        else
        {
            PanelEmptyDiff.IsVisible = true;
        }

        UpdateStagingCount();
        TxtStatusMessage.Text = $"Comparação concluída: {_diffResult.Summary}";
    }

    private void FilterTableList()
    {
        var filter = TxtFilterTable.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        var items = string.IsNullOrEmpty(filter)
            ? _allTableDiffs
            : _allTableDiffs.Where(t => t.FullName.ToLowerInvariant().Contains(filter)).ToList();

        GridTableDiffs.ItemsSource = items;
    }

    private void OnTableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GridTableDiffs.SelectedItem is TableDiffItem selected)
        {
            TxtInspectorTableName.Text = selected.FullName;
            var nonIdenticalCols = selected.ColumnDiffs.Count(c => c.Status != DiffStatus.Identical);
            TxtInspectorColCount.Text = $"{selected.ColumnDiffs.Count} colunas ({nonIdenticalCols} divergente(s))";
            GridColumnDiffs.ItemsSource = selected.ColumnDiffs;
        }
        else
        {
            TxtInspectorTableName.Text = "(Nenhuma tabela selecionada)";
            TxtInspectorColCount.Text = "0 colunas divergentes";
            GridColumnDiffs.ItemsSource = null;
        }

        UpdateStagingCount();
    }

    private void SetSelectionForAll(bool isSelected)
    {
        foreach (var item in _allTableDiffs)
        {
            item.IsSelectedForSync = isSelected;
        }
        GridTableDiffs.ItemsSource = null;
        FilterTableList();
        UpdateStagingCount();
    }

    private void SelectAdditionsOnly()
    {
        foreach (var item in _allTableDiffs)
        {
            item.IsSelectedForSync = item.Status == DiffStatus.Added;
        }
        GridTableDiffs.ItemsSource = null;
        FilterTableList();
        UpdateStagingCount();
    }

    private void UpdateStagingCount()
    {
        var count = _allTableDiffs.Count(t => t.IsSelectedForSync);
        TxtStagingCount.Text = $"{count} tabela(s) selecionada(s)";
    }

    private void OpenSyncPreview()
    {
        if (_diffResult == null)
        {
            TxtStatusMessage.Text = "Execute uma comparação de schema antes de gerar o script de sincronização.";
            return;
        }

        var selectedTables = _allTableDiffs.Where(t => t.IsSelectedForSync).Select(t => t.Name).ToHashSet();
        if (selectedTables.Count == 0)
        {
            TxtStatusMessage.Text = "Nenhuma tabela foi marcada para sincronização.";
            return;
        }

        var tgtEndpoint = (CboTarget.SelectedItem as CompareEndpointItem)?.Endpoint;
        bool isOffline = tgtEndpoint?.IsOfflineSnapshot ?? false;

        var targetDialect = tgtEndpoint?.Dialect ?? (_targetSnapshot?.EngineType == "SQLite" ? DatabaseType.SQLite : DatabaseType.PostgreSQL);
        var safeMode = ChkSafeMode.IsChecked == true;

        var options = new SyncOptions
        {
            TargetDialect = targetDialect,
            SafeMode = safeMode,
            Direction = SyncDirection.SourceToTarget,
            SelectedTableNames = selectedTables
        };

        var ddl = _diffService.GenerateSyncDdl(_diffResult, options);

        ModalContainer.IsVisible = false;
        PanelIntegrityModal.IsVisible = false;
        ModalSyncPreview.IsVisible = true;
        ModalSyncPreview.Setup(ddl, safeMode, isTargetOfflineSnapshot: isOffline);
        PanelModalOverlay.IsVisible = true;
    }

    private async Task ExecuteSyncOnTargetAsync()
    {
        if (_connectedTargetDb == null)
        {
            ModalSyncPreview.SetStatus("Destino é um snapshot offline. Use 'Salvar Snapshot Atualizado' ou 'Copiar DDL'.", isError: true);
            return;
        }

        var selectedTables = _allTableDiffs.Where(t => t.IsSelectedForSync).Select(t => t.Name).ToHashSet();
        var options = new SyncOptions
        {
            TargetDialect = _connectedTargetDb.Config.Type,
            SafeMode = ChkSafeMode.IsChecked == true,
            Direction = SyncDirection.SourceToTarget,
            SelectedTableNames = selectedTables
        };

        var ddl = _diffService.GenerateSyncDdl(_diffResult!, options);

        ModalSyncPreview.SetStatus("Aplicando alterações no banco de destino...");
        var result = await _diffService.ApplySyncDdlAsync(_connectedTargetDb, ddl);

        if (result.Success)
        {
            ModalSyncPreview.SetStatus("✓ Sincronização executada com sucesso no destino!", isError: false);
            TxtStatusMessage.Text = "✓ Alterações de schema aplicadas com sucesso.";
            await ExecuteComparisonAsync();
        }
        else
        {
            ModalSyncPreview.SetStatus($"Erro ao aplicar DDL no destino: {result.ErrorMessage}", isError: true);
        }
    }

    private async Task SaveUpdatedSnapshotAsync()
    {
        if (_diffResult == null || _targetSnapshot == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar Snapshot Atualizado (.rschema)",
            DefaultExtension = "rschema",
            SuggestedFileName = $"{_targetSnapshot.DatabaseName}_synced_{DateTime.Now:yyyyMMdd_HHmmss}.rschema"
        });

        if (file != null)
        {
            // Synthesize synchronized snapshot
            var updatedSnapshot = new SchemaSnapshotModel
            {
                DatabaseName = _targetSnapshot.DatabaseName,
                EngineType = _targetSnapshot.EngineType,
                Label = $"{_targetSnapshot.Label} (Synced {DateTime.UtcNow:yyyy-MM-dd})",
                CreatedAt = DateTime.UtcNow
            };

            // Copy all source tables that were selected for sync
            var selectedSet = _allTableDiffs.Where(t => t.IsSelectedForSync).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var table in _sourceSnapshot!.Tables)
            {
                if (selectedSet.Contains(table.Name))
                {
                    updatedSnapshot.Tables.Add(table);
                }
            }

            var path = file.Path.LocalPath;
            await SchemaSnapshotStorageService.Instance.SaveSnapshotAsync(path, updatedSnapshot);
            ModalSyncPreview.SetStatus($"✓ Snapshot atualizado salvo em {file.Name}");
            TxtStatusMessage.Text = $"✓ Snapshot salvo: {file.Name}";
        }
    }

    public async Task LoadSnapshotFileDirectAsync(string filePath)
    {
        await LoadSnapshotCoreAsync(filePath, isSource: false, allowCompromised: false);
    }

    private void OpenCaptureSnapshotDialog()
    {
        var dialog = new CaptureSnapshotDialog(_sourceDb);
        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.IsVisible = false;
            ModalContainer.Content = null;
        };
        dialog.SnapshotCaptured += (path, envelope) =>
        {
            TxtStatusMessage.Text = $"✓ Snapshot capturado: {Path.GetFileName(path)}";
            PopulateEndpointCombos();
        };

        ModalSyncPreview.IsVisible = false;
        PanelIntegrityModal.IsVisible = false;
        ModalContainer.Content = dialog;
        ModalContainer.IsVisible = true;
        PanelModalOverlay.IsVisible = true;
    }

    /// <summary>
    /// Emula uma comparação realista de schemas para testes e screenshot headless.
    /// </summary>
    public void EmulateDiffForTesting()
    {
        var src = new SchemaSnapshotModel
        {
            EngineType = "PostgreSQL",
            DatabaseName = "ecommerce_production",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "customers",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "name", DataType = "varchar(100)", IsNullable = false },
                        new() { Name = "email", DataType = "varchar(150)", IsNullable = false }
                    }
                },
                new()
                {
                    Name = "orders",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "customer_id", DataType = "integer", IsNullable = false },
                        new() { Name = "order_date", DataType = "timestamp", IsNullable = false },
                        new() { Name = "tracking_code", DataType = "varchar(100)", IsNullable = true },
                        new() { Name = "status", DataType = "varchar(50)", IsNullable = false }
                    }
                },
                new()
                {
                    Name = "invoices",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "order_id", DataType = "integer", IsNullable = false },
                        new() { Name = "amount", DataType = "numeric(10,2)", IsNullable = false },
                        new() { Name = "due_date", DataType = "date", IsNullable = false },
                        new() { Name = "status", DataType = "varchar(30)", IsNullable = false }
                    }
                }
            }
        };

        var tgt = new SchemaSnapshotModel
        {
            EngineType = "PostgreSQL",
            DatabaseName = "ecommerce_staging",
            Tables = new List<TableSnapshot>
            {
                new()
                {
                    Name = "customers",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "name", DataType = "varchar(100)", IsNullable = false },
                        new() { Name = "email", DataType = "varchar(150)", IsNullable = false }
                    }
                },
                new()
                {
                    Name = "orders",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "customer_id", DataType = "integer", IsNullable = false },
                        new() { Name = "order_date", DataType = "timestamp", IsNullable = false },
                        new() { Name = "status", DataType = "integer", IsNullable = false }
                    }
                },
                new()
                {
                    Name = "legacy_logs",
                    Columns = new List<ColumnSnapshot>
                    {
                        new() { Name = "id", DataType = "integer", IsPrimaryKey = true },
                        new() { Name = "log_time", DataType = "timestamp", IsNullable = false },
                        new() { Name = "message", DataType = "text", IsNullable = true }
                    }
                }
            }
        };

        _sourceSnapshot = src;
        _targetSnapshot = tgt;

        var epSrc = new CompareEndpoint { Type = CompareEndpointType.ActiveConnection, DisplayName = "[PostgreSQL] ecommerce_production" };
        var epTgt = new CompareEndpoint { Type = CompareEndpointType.SavedConnection, DisplayName = "🐘 ecommerce_staging [PostgreSQL]" };

        CboSource.Items.Clear();
        CboSource.Items.Add(new CompareEndpointItem(epSrc));
        CboSource.SelectedIndex = 0;

        CboTarget.Items.Clear();
        CboTarget.Items.Add(new CompareEndpointItem(epTgt));
        CboTarget.SelectedIndex = 0;

        RunDiffAndDisplay();

        var ordersItem = _allTableDiffs.FirstOrDefault(t => t.Name == "orders");
        if (ordersItem != null)
        {
            GridTableDiffs.SelectedItem = ordersItem;
        }
    }

    public void Dispose()
    {
        GridTableDiffs.ItemsSource = null;
        GridColumnDiffs.ItemsSource = null;
        _allTableDiffs.Clear();
        _sourceSnapshot = null;
        _targetSnapshot = null;
        _diffResult = null;
    }
}
