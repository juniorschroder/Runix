using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Export;
using Runix.Infrastructure.Migrations;

namespace Runix.Desktop.Views;

public partial class ErdTabControl : UserControl, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly ISchemaMigrationEngine _migrationEngine = new SchemaMigrationEngine();
    private readonly DdlGeneratorService _ddlGenerator = new();
    private readonly IErdExportService _exportService = new ErdExportService();
    private ErdGraphModel _graph = new();

    public event Action? TableCreated;

    public ErdTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public ErdTabControl(IDatabaseService databaseService)
    {
        InitializeComponent();
        Focusable = true;
        _databaseService = databaseService;

        CanvasErd.CurrentDatabaseId = string.IsNullOrWhiteSpace(_databaseService.Config.FilePath)
            ? _databaseService.Config.Name
            : _databaseService.Config.FilePath;

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

        // Auto-layout via Sugiyama com transição animada (~250ms)
        BtnAutoLayout.Click += async (s, e) =>
        {
            await CanvasErd.AnimateLayoutTransitionAsync();
        };

        // Fast Search Overlay (Ctrl/Cmd+F)
        BtnSearch.Click += (s, e) => OverlaySearch.OpenAndFocus();
        OverlaySearch.ItemSelected += (tableName, colName) =>
        {
            CanvasErd.PanToNode(tableName);
            CanvasErd.HighlightNodePulse(tableName);
        };

        // Global Keyboard Shortcuts
        KeyDown += async (s, e) =>
        {
            bool isCtrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (isCtrlOrCmd && e.Key == Key.F)
            {
                OverlaySearch.OpenAndFocus();
                e.Handled = true;
            }
            else if (isCtrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.L)
            {
                await CanvasErd.AnimateLayoutTransitionAsync();
                e.Handled = true;
            }
            else if (isCtrlOrCmd && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.E)
            {
                OpenExportDialog();
                e.Handled = true;
            }
        };

        // Modal triggers: Nova Tabela
        BtnNewTable.Click += (s, e) =>
        {
            DialogTableEditor.SetModeNewTable(_databaseService.Config.Type);
            OverlayTableEditor.IsVisible = true;
        };

        // Modal triggers: Nova Área de Domínio (Subject Area)
        BtnNewSubjectArea.Click += (s, e) =>
        {
            double defaultX = 80;
            double defaultY = 80;

            if (_graph.Nodes.Count > 0)
            {
                var minX = _graph.Nodes.Min(n => n.X);
                var minY = _graph.Nodes.Min(n => n.Y);
                defaultX = Math.Max(40, minX - 40);
                defaultY = Math.Max(40, minY - 40);
            }

            DialogCreateSubjectArea.Reset(defaultX, defaultY);
            OverlaySubjectArea.IsVisible = true;
        };

        // CreateSubjectAreaDialog callbacks
        DialogCreateSubjectArea.CancelRequested += () => OverlaySubjectArea.IsVisible = false;
        DialogCreateSubjectArea.ConfirmRequested += area =>
        {
            OverlaySubjectArea.IsVisible = false;
            CanvasErd.AddSubjectArea(area);
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

        // Export Modal Action
        BtnExportDiagram.Click += (s, e) => OpenExportDialog();
        DialogExport.CancelRequested += () => OverlayExport.IsVisible = false;
        DialogExport.ExportConfirmed += async opt => await HandleExportAsync(opt);

        // Retry Action
        BtnRetryLoad.Click += async (s, e) => await ReloadGraphAsync();
    }

    public async Task ReloadGraphAsync()
    {
        BannerError.IsVisible = false;
        ProgressLoading.IsVisible = true;
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _graph = await _databaseService.GetErdGraphAsync();
                CanvasErd.SetGraph(_graph);
                OverlaySearch.SetGraph(_graph);
                BannerError.IsVisible = false;
                break;
            }
            catch (Exception ex) when (attempt < maxRetries && (ex.Message.Contains("progress", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"[ErdTabControl] Retrying graph reload (attempt {attempt}/{maxRetries}): {ex.Message}");
                await Task.Delay(250 * attempt);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ErdTabControl] Error loading graph: {ex.Message}");
                if (attempt == maxRetries)
                {
                    TxtErrorMessage.Text = $"Falha ao carregar diagrama: {ex.Message}";
                    BannerError.IsVisible = true;
                }
            }
        }

        ProgressLoading.IsVisible = false;
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

                if (file == null)
                {
                    return;
                }

                destinationPath = file.Path.LocalPath;
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

    public void EmulateSearchOverlayForTesting(string? query = null)
    {
        OverlaySearch.OpenAndFocus();
        if (!string.IsNullOrEmpty(query))
        {
            OverlaySearch.TxtSearch.Text = query;
        }
    }

    public void OpenExportDialog()
    {
        DialogExport.Setup(_graph);
        OverlayExport.IsVisible = true;
    }

    public void CloseExportDialog()
    {
        OverlayExport.IsVisible = false;
    }

    public void EmulateExportDialogForTesting()
    {
        OpenExportDialog();
    }

    private async Task HandleExportAsync(ErdExportOptions options)
    {
        try
        {
            if (options.Format == ErdExportFormat.Sql)
            {
                await OnExportSqlAsync();
                return;
            }

            if (options.Format == ErdExportFormat.Png)
            {
                await OnExportPngAsync();
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            string ext = options.Format == ErdExportFormat.Svg ? "svg" : "pdf";

            string? destinationPath = null;
            if (topLevel?.StorageProvider != null && topLevel.StorageProvider.CanSave)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export ERD Diagram as {ext.ToUpperInvariant()}",
                    DefaultExtension = ext,
                    SuggestedFileName = $"erd_{_databaseService.Config.Name}_{DateTime.Now:yyyyMMdd_HHmm}.{ext}"
                });

                if (file == null)
                {
                    // User canceled the file picker dialog
                    return;
                }

                destinationPath = file.Path.LocalPath;
            }

            if (string.IsNullOrEmpty(destinationPath))
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                destinationPath = Path.Combine(dir, $"erd_{_databaseService.Config.Name}.{ext}");
            }

            await _exportService.ExportToFileAsync(destinationPath, _graph, options);
            Console.WriteLine($"[ErdTabControl] Diagram successfully exported to {destinationPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ErdTabControl] Export error: {ex.Message}");
        }
        finally
        {
            // The export dialog is automatically closed after export completes (or is dismissed)
            CloseExportDialog();
        }
    }

    public void Dispose()
    {
        _graph.Nodes.Clear();
        _graph.Relationships.Clear();
        _graph.SubjectAreas.Clear();
        CanvasErd.Clear();
    }
}
