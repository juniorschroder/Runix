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
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Diff;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Diff;

public partial class SchemaCompareTabControl : UserControl
{
    private readonly IDatabaseService _sourceDb;
    private readonly ISchemaDiffService _diffService;
    private readonly List<ConnectionConfig> _savedConnections = new();

    private SchemaSnapshotModel? _sourceSnapshot;
    private SchemaSnapshotModel? _targetSnapshot;
    private SchemaDiffResult? _diffResult;
    private List<TableDiffItem> _allTableDiffs = new();
    private bool _isDirectionInverted;
    private IDatabaseService? _connectedTargetDb;
    private string? _loadedSnapshotPath;

    public SchemaCompareTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public SchemaCompareTabControl(IDatabaseService sourceDb)
    {
        _sourceDb = sourceDb;
        _diffService = new SchemaDiffService();

        InitializeComponent();

        SetupSourceInfo();
        LoadTargetOptions();
        SetupEvents();
        UpdateStagingCount();
    }

    private void SetupSourceInfo()
    {
        var dbName = _sourceDb.Config.Type == DatabaseType.SQLite
            ? Path.GetFileName(_sourceDb.Config.FilePath)
            : $"{_sourceDb.Config.Database} ({_sourceDb.Config.Host})";

        TxtSourceBadge.Text = $"[{_sourceDb.Config.Type}] {dbName}";
    }

    private void LoadTargetOptions()
    {
        _savedConnections.Clear();
        CboTarget.Items.Clear();

        var loaded = ConnectionStorageService.LoadConnections();
        _savedConnections.AddRange(loaded);

        foreach (var conn in _savedConnections)
        {
            var label = $"{conn.EngineIcon} {conn.Name} [{conn.Type}]";
            CboTarget.Items.Add(new ComboBoxItem { Content = label, Tag = conn });
        }

        if (CboTarget.Items.Count > 0)
        {
            CboTarget.SelectedIndex = 0;
        }
    }

    private void SetupEvents()
    {
        BtnSaveSnapshot.Click += async (s, e) => await SaveSourceSnapshotAsync();
        BtnLoadSnapshotFile.Click += async (s, e) => await LoadSnapshotFileAsync();
        BtnInvertDirection.Click += (s, e) => ToggleDirection();
        BtnCompare.Click += async (s, e) => await ExecuteComparisonAsync();

        BtnSelectAll.Click += (s, e) => SetSelectionForAll(true);
        BtnDeselectAll.Click += (s, e) => SetSelectionForAll(false);
        BtnSelectAdditions.Click += (s, e) => SelectAdditionsOnly();

        TxtFilterTable.TextChanged += (s, e) => FilterTableList();
        GridTableDiffs.SelectionChanged += OnTableSelectionChanged;

        BtnGenerateSync.Click += (s, e) => OpenSyncPreview();

        ModalSyncPreview.CloseRequested += () => PanelModalOverlay.IsVisible = false;
        ModalSyncPreview.ExecuteRequested += async () => await ExecuteSyncOnTargetAsync();
    }

    private void ToggleDirection()
    {
        _isDirectionInverted = !_isDirectionInverted;

        if (_isDirectionInverted)
        {
            TxtDirection.Text = "Destino ➔ Origem";
            TxtDirection.Foreground = Avalonia.Media.Brush.Parse("#fbbf24");
        }
        else
        {
            TxtDirection.Text = "Origem ➔ Destino";
            TxtDirection.Foreground = Avalonia.Media.Brush.Parse("#f0f6fc");
        }

        if (_sourceSnapshot != null && _targetSnapshot != null)
        {
            RunDiffAndDisplay();
        }
    }

    private async Task SaveSourceSnapshotAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Snapshot de Schema JSON",
            DefaultExtension = "json",
            SuggestedFileName = $"snapshot_{_sourceDb.Config.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        });

        if (file != null)
        {
            TxtStatusMessage.Text = "Capturando snapshot do schema atual...";
            var snapshot = await _diffService.ExtractSnapshotAsync(_sourceDb);
            var json = _diffService.SerializeSnapshot(snapshot);
            await File.WriteAllTextAsync(file.Path.LocalPath, json);
            TxtStatusMessage.Text = $"✓ Snapshot salvo em {file.Name}";
        }
    }

    private async Task LoadSnapshotFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar Snapshot de Schema JSON",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Schema Snapshot JSON") { Patterns = new[] { "*.json" } }
            }
        });

        if (files.Count > 0)
        {
            var filePath = files[0].Path.LocalPath;
            _loadedSnapshotPath = filePath;
            _connectedTargetDb = null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                _targetSnapshot = _diffService.DeserializeSnapshot(json);
                CboTarget.Items.Insert(0, new ComboBoxItem
                {
                    Content = $"📄 [Arquivo] {files[0].Name}",
                    Tag = "FILE_SNAPSHOT"
                });
                CboTarget.SelectedIndex = 0;
                TxtStatusMessage.Text = $"Snapshot carregado de {files[0].Name} ({_targetSnapshot.Tables.Count} tabelas).";
            }
            catch (Exception ex)
            {
                TxtStatusMessage.Text = $"Erro ao carregar snapshot JSON: {ex.Message}";
            }
        }
    }

    public async Task ExecuteComparisonAsync()
    {
        TxtStatusMessage.Text = "Inspecionando schemas e executando diff...";
        PanelEmptyDiff.IsVisible = false;

        try
        {
            // 1. Capture source snapshot
            _sourceSnapshot ??= await _diffService.ExtractSnapshotAsync(_sourceDb);

            // 2. Prepare target snapshot
            if (CboTarget.SelectedItem is ComboBoxItem item && item.Tag is ConnectionConfig targetConfig)
            {
                _connectedTargetDb?.Dispose();
                _connectedTargetDb = targetConfig.Type == DatabaseType.SQLite
                    ? new SqliteDatabaseService(targetConfig)
                    : new PostgresDatabaseService(targetConfig);

                await _connectedTargetDb.ConnectAsync();
                _targetSnapshot = await _diffService.ExtractSnapshotAsync(_connectedTargetDb);
            }
            else if (_targetSnapshot == null && !string.IsNullOrEmpty(_loadedSnapshotPath))
            {
                var json = await File.ReadAllTextAsync(_loadedSnapshotPath);
                _targetSnapshot = _diffService.DeserializeSnapshot(json);
            }

            if (_targetSnapshot == null)
            {
                TxtStatusMessage.Text = "Selecione um banco de destino ou carregue um snapshot JSON para comparar.";
                return;
            }

            RunDiffAndDisplay();
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Falha ao comparar schemas: {ex.Message}";
        }
    }

    private void RunDiffAndDisplay()
    {
        if (_sourceSnapshot == null || _targetSnapshot == null) return;

        _diffResult = _diffService.CompareSnapshots(_sourceSnapshot, _targetSnapshot, _isDirectionInverted);
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

        var targetDialect = _isDirectionInverted
            ? _sourceDb.Config.Type
            : (_connectedTargetDb?.Config.Type ?? (_targetSnapshot?.EngineType == "SQLite" ? DatabaseType.SQLite : DatabaseType.PostgreSQL));

        var safeMode = ChkSafeMode.IsChecked == true;

        var options = new SyncOptions
        {
            TargetDialect = targetDialect,
            SafeMode = safeMode,
            Direction = _isDirectionInverted ? SyncDirection.TargetToSource : SyncDirection.SourceToTarget,
            SelectedTableNames = selectedTables
        };

        var ddl = _diffService.GenerateSyncDdl(_diffResult, options);

        ModalSyncPreview.Setup(ddl, safeMode);
        PanelModalOverlay.IsVisible = true;
    }

    private async Task ExecuteSyncOnTargetAsync()
    {
        if (_connectedTargetDb == null)
        {
            ModalSyncPreview.SetStatus("Destino é um snapshot offline. Copie ou salve o script para executá-lo manualmente no banco.", isError: true);
            return;
        }

        var selectedTables = _allTableDiffs.Where(t => t.IsSelectedForSync).Select(t => t.Name).ToHashSet();
        var options = new SyncOptions
        {
            TargetDialect = _connectedTargetDb.Config.Type,
            SafeMode = ChkSafeMode.IsChecked == true,
            Direction = _isDirectionInverted ? SyncDirection.TargetToSource : SyncDirection.SourceToTarget,
            SelectedTableNames = selectedTables
        };

        var ddl = _diffService.GenerateSyncDdl(_diffResult!, options);

        ModalSyncPreview.SetStatus("Aplicando alterações no banco de destino...");
        var result = await _diffService.ApplySyncDdlAsync(_connectedTargetDb, ddl);

        if (result.Success)
        {
            ModalSyncPreview.SetStatus("✓ Sincronização executada com sucesso no destino!", isError: false);
            TxtStatusMessage.Text = "✓ Alterações de schema aplicadas com sucesso.";
            // Re-run comparison to refresh state
            await ExecuteComparisonAsync();
        }
        else
        {
            ModalSyncPreview.SetStatus($"Erro ao aplicar DDL no destino: {result.ErrorMessage}", isError: true);
        }
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
                        new() { Name = "tracking_code", DataType = "varchar(100)", IsNullable = true }, // New column
                        new() { Name = "status", DataType = "varchar(50)", IsNullable = false } // Modified type
                    }
                },
                new()
                {
                    Name = "invoices", // New table
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
                        new() { Name = "status", DataType = "integer", IsNullable = false } // Target has integer status
                    }
                },
                new()
                {
                    Name = "legacy_logs", // Extra in target
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

        TxtSourceBadge.Text = "[PostgreSQL] ecommerce_production";
        CboTarget.Items.Clear();
        CboTarget.Items.Add(new ComboBoxItem { Content = "🐘 ecommerce_staging [PostgreSQL]" });
        CboTarget.SelectedIndex = 0;

        RunDiffAndDisplay();

        // Select the modified 'orders' table to showcase the inspector in the screenshot
        var ordersItem = _allTableDiffs.FirstOrDefault(t => t.Name == "orders");
        if (ordersItem != null)
        {
            GridTableDiffs.SelectedItem = ordersItem;
        }
    }
}
