using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Migrations;

namespace Runix.Desktop.Views;

public partial class ErdTabControl : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly ISchemaMigrationEngine _migrationEngine = new SchemaMigrationEngine();
    private readonly DdlGeneratorService _ddlGenerator = new();
    private ErdGraphModel _graph = new();

    public event Action? TableCreated;

    public ErdTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public ErdTabControl(IDatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;

        SetupEvents();
        _ = ReloadGraphAsync();
    }

    private void SetupEvents()
    {
        // Zoom controls
        BtnZoomIn.Click += (s, e) => CanvasErd.ZoomIn();
        BtnZoomOut.Click += (s, e) => CanvasErd.ZoomOut();
        BtnResetZoom.Click += (s, e) => CanvasErd.ResetView();
        CanvasErd.ZoomChanged += z => TxtZoomPercent.Text = $"{(int)(z * 100)}%";

        // Auto-layout
        BtnAutoLayout.Click += (s, e) =>
        {
            ErdLayoutService.ApplyTopologicalLayout(_graph);
            CanvasErd.RefreshLayout();
            CanvasErd.CenterOnContent();
            var dbId = string.IsNullOrWhiteSpace(_databaseService.Config.FilePath)
                ? _databaseService.Config.Name
                : _databaseService.Config.FilePath;
            ErdLayoutService.SaveLayout(dbId, _graph);
        };

        // Modal triggers: Nova Tabela
        BtnNewTable.Click += (s, e) =>
        {
            DialogTableEditor.SetModeNewTable(_databaseService.Config.Type);
            OverlayTableEditor.IsVisible = true;
        };

        // Canvas Events: Editar Tabela
        CanvasErd.EditTableRequested += node =>
        {
            DialogTableEditor.SetModeEditTable(node, _databaseService.Config.Type);
            OverlayTableEditor.IsVisible = true;
        };

        // Canvas Events: Criar Conexão FK
        CanvasErd.ConnectionRequested += (srcNode, srcCol, tgtNode, tgtCol) =>
        {
            DialogForeignKey.Setup(_graph, srcNode.TableName, srcCol.Name, tgtNode.TableName, tgtCol?.Name);
            OverlayForeignKey.IsVisible = true;
        };

        // TableEditorDialog callbacks
        DialogTableEditor.CancelRequested += () => OverlayTableEditor.IsVisible = false;
        DialogTableEditor.ConfirmRequested += diff =>
        {
            OverlayTableEditor.IsVisible = false;
            var plan = _migrationEngine.CreatePlan(diff, _databaseService.Config.Type);
            DialogMigrationPreview.SetPlan(plan);
            OverlayMigrationPreview.IsVisible = true;
        };

        // ForeignKeyDialog callbacks
        DialogForeignKey.CancelRequested += () => OverlayForeignKey.IsVisible = false;
        DialogForeignKey.ConfirmRequested += fk =>
        {
            OverlayForeignKey.IsVisible = false;
            var plan = _migrationEngine.CreateForeignKeyPlan(fk, _databaseService.Config.Type);
            DialogMigrationPreview.SetPlan(plan);
            OverlayMigrationPreview.IsVisible = true;
        };

        // MigrationPreviewDialog callbacks
        DialogMigrationPreview.CancelRequested += () => OverlayMigrationPreview.IsVisible = false;
        DialogMigrationPreview.ExecuteRequested += async plan =>
        {
            var res = await _migrationEngine.ExecutePlanAsync(_databaseService, plan);
            if (res.Success)
            {
                OverlayMigrationPreview.IsVisible = false;
                await ReloadGraphAsync();
                TableCreated?.Invoke();
            }
            else
            {
                DialogMigrationPreview.ShowError(res.ErrorMessage ?? "Erro desconhecido na execução da migração.");
            }
        };

        // Export Actions
        BtnExportPng.Click += async (s, e) => await OnExportPngAsync();
        BtnExportSql.Click += async (s, e) => await OnExportSqlAsync();
    }

    public async Task ReloadGraphAsync()
    {
        ProgressLoading.IsVisible = true;
        try
        {
            _graph = await _databaseService.GetErdGraphAsync();
            CanvasErd.SetGraph(_graph);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErdTabControl] Error loading graph: {ex.Message}");
        }
        finally
        {
            ProgressLoading.IsVisible = false;
        }
    }

    private async Task OnExportPngAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            string? destinationPath = null;

            if (topLevel?.StorageProvider != null && topLevel.StorageProvider.CanSave)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export ERD Diagram as PNG",
                    DefaultExtension = "png",
                    SuggestedFileName = $"erd_{_databaseService.Config.Name}_{DateTime.Now:yyyyMMdd_HHmm}.png"
                });
                destinationPath = file?.Path.LocalPath;
            }

            if (string.IsNullOrEmpty(destinationPath))
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                destinationPath = Path.Combine(dir, $"erd_{_databaseService.Config.Name}.png");
            }

            var width = (int)Math.Max(1200, CanvasErd.Bounds.Width);
            var height = (int)Math.Max(800, CanvasErd.Bounds.Height);
            var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            rtb.Render(CanvasErd);
            rtb.Save(destinationPath);
            Console.WriteLine($"[ErdTabControl] Diagram exported to {destinationPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErdTabControl] Failed to export PNG: {ex.Message}");
        }
    }

    private async Task OnExportSqlAsync()
    {
        try
        {
            var sql = _ddlGenerator.GenerateFullSchemaSql(_graph, _databaseService.Config.Type);
            var topLevel = TopLevel.GetTopLevel(this);

            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(sql);
            }

            if (topLevel?.StorageProvider != null && topLevel.StorageProvider.CanSave)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Schema SQL DDL",
                    DefaultExtension = "sql",
                    SuggestedFileName = $"schema_{_databaseService.Config.Name}.sql"
                });

                if (file != null)
                {
                    await File.WriteAllTextAsync(file.Path.LocalPath, sql);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErdTabControl] Failed to export SQL: {ex.Message}");
        }
    }

    public void EmulateTableEditorForTesting()
    {
        DialogTableEditor.SetModeNewTable(_databaseService.Config.Type);
        OverlayTableEditor.IsVisible = true;
    }

    public void EmulateMigrationPreviewForTesting()
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = "pedidos", Schema = "public" },
            Columns = new System.Collections.Generic.List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "cliente_id", DataType = "INTEGER", IsNullable = false },
                new() { Name = "valor_total", DataType = "REAL", IsNullable = false }
            }
        };
        var diff = TableSchemaDiff.Calculate(null, node);
        var plan = _migrationEngine.CreatePlan(diff, _databaseService.Config.Type);
        DialogMigrationPreview.SetPlan(plan);
        OverlayMigrationPreview.IsVisible = true;
    }
}
