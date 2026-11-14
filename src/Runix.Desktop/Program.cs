using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Linq;

namespace Runix.Desktop;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string? screenshotPath = null;
        string? explicitLang = null;
        bool isDemo = false;
        bool isErd = false;
        bool isEditDemo = false;
        bool isCompletionDemo = false;
        bool isStructureDemo = false;
        bool isErdModeling = false;
        bool isMigrationPreview = false;
        bool isQueryLibraryDemo = false;
        bool isSnippetManagerDemo = false;
        bool isExportDialogDemo = false;
        bool isImportWizardDemo = false;
        bool isExplainPlanDemo = false;
        bool isDbaDemo = false;
        bool isCompareDemo = false;
        bool isChartDemo = false;
        bool isAboutDemo = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--screenshot" && i + 1 < args.Length)
            {
                screenshotPath = args[i + 1];
            }
            if (args[i] == "--lang" && i + 1 < args.Length)
            {
                explicitLang = args[i + 1];
            }
            if (args[i] == "--demo" || args[i] == "--workspace")
            {
                isDemo = true;
            }
            if (args[i] == "--erd")
            {
                isErd = true;
            }
            if (args[i] == "--erd-modeling-demo")
            {
                isErdModeling = true;
                isDemo = true;
            }
            if (args[i] == "--migration-preview-demo")
            {
                isMigrationPreview = true;
                isDemo = true;
            }
            if (args[i] == "--query-library-demo")
            {
                isQueryLibraryDemo = true;
                isDemo = true;
            }
            if (args[i] == "--snippet-manager-demo")
            {
                isSnippetManagerDemo = true;
                isDemo = true;
            }
            if (args[i] == "--export-dialog-demo")
            {
                isExportDialogDemo = true;
                isDemo = true;
            }
            if (args[i] == "--import-wizard-demo")
            {
                isImportWizardDemo = true;
                isDemo = true;
            }
            if (args[i] == "--explain-plan-demo")
            {
                isExplainPlanDemo = true;
                isDemo = true;
            }
            if (args[i] == "--dba-demo")
            {
                isDbaDemo = true;
                isDemo = true;
            }
            if (args[i] == "--compare-demo")
            {
                isCompareDemo = true;
                isDemo = true;
            }
            if (args[i] == "--chart-demo")
            {
                isChartDemo = true;
                isDemo = true;
            }
            if (args[i] == "--about-demo")
            {
                isAboutDemo = true;
                isDemo = true;
            }
            if (args[i] == "--edit-demo")
            {
                isEditDemo = true;
                isDemo = true;
            }
            if (args[i] == "--completion-demo")
            {
                isCompletionDemo = true;
                isDemo = true;
            }
            if (args[i] == "--structure-demo")
            {
                isStructureDemo = true;
                isDemo = true;
            }
        }

        if (!string.IsNullOrEmpty(screenshotPath))
        {
            // Headless capture - works in all terminal/CI environments without display server requirements
            RunHeadlessScreenshot(screenshotPath, isDemo, explicitLang, isErd, isEditDemo, isCompletionDemo, isStructureDemo, isErdModeling, isMigrationPreview, isQueryLibraryDemo, isSnippetManagerDemo, isExportDialogDemo, isImportWizardDemo, isExplainPlanDemo, isDbaDemo, isCompareDemo, isChartDemo, isAboutDemo);
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (InvalidOperationException)
        {
            // Fallback for headless environments if started without --screenshot
            Console.WriteLine("No active display detected; running in headless mode.");
            BuildHeadlessApp().SetupWithoutStarting();
        }
    }

    private static void RunHeadlessScreenshot(
        string screenshotPath, 
        bool isDemo, 
        string? explicitLang = null, 
        bool isErd = false, 
        bool isEditDemo = false, 
        bool isCompletionDemo = false, 
        bool isStructureDemo = false,
        bool isErdModeling = false,
        bool isMigrationPreview = false,
        bool isQueryLibraryDemo = false,
        bool isSnippetManagerDemo = false,
        bool isExportDialogDemo = false,
        bool isImportWizardDemo = false,
        bool isExplainPlanDemo = false,
        bool isDbaDemo = false,
        bool isCompareDemo = false,
        bool isChartDemo = false,
        bool isAboutDemo = false)
    {
        var app = BuildHeadlessApp().SetupWithoutStarting();
        Runix.Desktop.Services.LocalizationManager.Initialize(explicitLang);

        var window = new MainWindow();
        window.Width = 1200;
        window.Height = 800;

        if (isDemo || isErd || isEditDemo || isCompletionDemo || isStructureDemo || isErdModeling || isMigrationPreview || isQueryLibraryDemo || isSnippetManagerDemo || isExportDialogDemo || isImportWizardDemo || isExplainPlanDemo || isDbaDemo)
        {
            window.ConnectToDemoAsync().GetAwaiter().GetResult();
        }

        if (isImportWizardDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenTableDataTab("cheeses");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var demoDir = Path.Combine(Path.GetTempPath(), "runix_demo_import");
            Directory.CreateDirectory(demoDir);
            var demoCsv = Path.Combine(demoDir, "novos_queijos_artesanais.csv");
            File.WriteAllText(demoCsv,
                "name;type;price;rating;origin\n" +
                "Serra da Canastra Real;Semi-Duro;85.50;4.9;Brasil\n" +
                "Gouda Seleção Especial;Duro;92.00;4.8;Holanda\n" +
                "Parmesão Reserva 24m;Extra-Duro;120.00;5.0;Itália\n" +
                "Brie de Meaux Supreme;Mofo Branco;78.90;4.7;França\n" +
                "Camembert au Lait Cru;Mofo Branco;72.50;4.6;França\n"
            );

            var tableTab = window.CurrentWorkspaceView.FindTableDataTabControl("cheeses");
            var wizard = tableTab?.OpenImportDialog(demoCsv);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(400);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            wizard?.GoToStep(2);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(200);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        else if (isExportDialogDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenTableDataTab("cheeses");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var tableTab = window.CurrentWorkspaceView.FindTableDataTabControl("cheeses");
            tableTab?.OpenExportDialog();
        }
        else if (isQueryLibraryDemo && window.CurrentWorkspaceView != null)
        {
            var storage = Runix.Infrastructure.Storage.AppStorageService.Instance;
            storage.InitializeAsync().GetAwaiter().GetResult();

            storage.SaveQueryAsync(new Runix.Core.Models.SavedQuery
            {
                Title = "🧀 Queijos Mais Avaliados",
                Description = "Top queijos ordenados por nota e preço decrescente",
                SqlText = "SELECT name, type, price, rating FROM cheeses WHERE rating >= 4.5 ORDER BY price DESC;",
                Tags = new System.Collections.Generic.List<string> { "queijos", "relatorio", "top" },
                IsFavorite = true
            }).GetAwaiter().GetResult();

            storage.SaveQueryAsync(new Runix.Core.Models.SavedQuery
            {
                Title = "📊 Média de Preço por Tipo",
                Description = "Agrupamento de queijos por tipo e médias",
                SqlText = "SELECT type, COUNT(*) as total, AVG(price) as avg_price FROM cheeses GROUP BY type;",
                Tags = new System.Collections.Generic.List<string> { "metricas", "precos" },
                IsFavorite = false
            }).GetAwaiter().GetResult();

            storage.SaveQueryAsync(new Runix.Core.Models.SavedQuery
            {
                Title = "🔍 Busca de Queijos Artesanais",
                Description = "Queijos de produtores locais artesanais",
                SqlText = "SELECT * FROM cheeses WHERE description LIKE '%artesanal%';",
                Tags = new System.Collections.Generic.List<string> { "artesanal", "busca" },
                IsFavorite = true
            }).GetAwaiter().GetResult();

            storage.LogQueryExecutionAsync(new Runix.Core.Models.QueryHistoryEntry
            {
                SqlText = "SELECT * FROM cheeses LIMIT 10;",
                ElapsedMilliseconds = 14,
                RowCount = 10,
                Success = true
            }).GetAwaiter().GetResult();

            storage.LogQueryExecutionAsync(new Runix.Core.Models.QueryHistoryEntry
            {
                SqlText = "SELECT COUNT(*) FROM invalid_table;",
                ElapsedMilliseconds = 6,
                RowCount = 0,
                Success = false,
                ErrorMessage = "no such table: invalid_table"
            }).GetAwaiter().GetResult();

            window.CurrentWorkspaceView.SwitchToQueriesTab();
            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            if (queryTab != null)
            {
                queryTab.TxtSqlEditor.Text = "SELECT name, type, price, rating\nFROM cheeses\nWHERE rating >= 4.5\nORDER BY price DESC;";
            }
        }
        else if (isSnippetManagerDemo && window.CurrentWorkspaceView != null)
        {
            var storage = Runix.Infrastructure.Storage.AppStorageService.Instance;
            storage.InitializeAsync().GetAwaiter().GetResult();

            storage.SaveCustomSnippetAsync(new Runix.Core.Models.CustomSnippet
            {
                Prefix = "selcheese",
                Title = "Select All Cheeses",
                Description = "Consulta rápida na tabela de queijos com limite",
                Body = "SELECT id, name, type, price\nFROM cheeses\nORDER BY name ASC\nLIMIT 50;"
            }).GetAwaiter().GetResult();

            storage.SaveCustomSnippetAsync(new Runix.Core.Models.CustomSnippet
            {
                Prefix = "myjoin",
                Title = "Inner Join Produtores",
                Description = "Join entre queijos e tabela de fornecedores",
                Body = "INNER JOIN producers p ON p.id = c.producer_id"
            }).GetAwaiter().GetResult();

            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            queryTab?.OpenSnippetManagerDialog();
        }
        else if (isErdModeling && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenErdTab();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var erdTab = window.CurrentWorkspaceView.GetActiveErdTabControl();
            erdTab?.EmulateTableEditorForTesting();
        }
        else if (isMigrationPreview && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenErdTab();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var erdTab = window.CurrentWorkspaceView.GetActiveErdTabControl();
            erdTab?.EmulateMigrationPreviewForTesting();
        }
        else if (isStructureDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenTableStructureTab("cheeses");
        }
        else if (isExplainPlanDemo && window.CurrentWorkspaceView != null)
        {
            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            if (queryTab == null)
            {
                window.CurrentWorkspaceView.OpenNewQueryTab();
                queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            }

            if (queryTab != null)
            {
                queryTab.TxtSqlEditor.Text = "SELECT * FROM cheeses WHERE price > 60.0 ORDER BY rating DESC;";
                queryTab.RunExplainAsync(analyze: false).GetAwaiter().GetResult();
            }
        }
        else if (isDbaDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenDbaTab();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var dbaTab = window.CurrentWorkspaceView.GetActiveDbaTabControl();
            dbaTab?.EmulateDbaForTesting();
        }
        else if (isCompareDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenCompareTab();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var compareTab = window.CurrentWorkspaceView.GetActiveCompareTabControl();
            compareTab?.EmulateDiffForTesting();
        }
        else if (isChartDemo && window.CurrentWorkspaceView != null)
        {
            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            if (queryTab == null)
            {
                window.CurrentWorkspaceView.OpenNewQueryTab();
                queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            }

            if (queryTab != null)
            {
                queryTab.ViewChart.EmulateChartForTesting();
                queryTab.SwitchBottomTab(1); // Switch to Chart tab
            }
        }
        else if (isDemo && !isErd && !isEditDemo && !isCompletionDemo && !isExplainPlanDemo && !isDbaDemo && !isCompareDemo && !isChartDemo && window.CurrentWorkspaceView != null)
        {
            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            if (queryTab != null)
            {
                queryTab.TxtSqlEditor.Text = "SELECT * FROM cheeses;";
                queryTab.ExecuteSql();
            }
            if (isAboutDemo)
            {
                window.OpenAboutDialog();
            }
        }

        if (isErd && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenErdTab();
        }
        else if (isEditDemo && window.CurrentWorkspaceView != null)
        {
            window.CurrentWorkspaceView.OpenTableDataTab("cheeses");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var tableTab = window.CurrentWorkspaceView.FindTableDataTabControl("cheeses");
            tableTab?.EmulateStagedEditsForTesting();
        }
        else if (isCompletionDemo && window.CurrentWorkspaceView != null)
        {
            var queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            if (queryTab == null)
            {
                window.CurrentWorkspaceView.OpenNewQueryTab();
                queryTab = window.CurrentWorkspaceView.GetActiveQueryTabControl();
            }

            if (queryTab != null)
            {
                queryTab.InitializeSchemaCacheAsync().GetAwaiter().GetResult();

                queryTab.TxtSqlEditor.Text = "SELECT c. FROM cheeses c;";
                queryTab.TxtSqlEditor.CaretOffset = "SELECT c.".Length;

                queryTab.TriggerCompletionEvaluation(force: true);
            }
        }

        window.Show();

        // Process layout and render jobs
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        System.Threading.Thread.Sleep(500);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Capture frame
        var frame = window.CaptureRenderedFrame();
        if (frame != null)
        {
            var fullPath = Path.GetFullPath(screenshotPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            frame.Save(fullPath);
            Console.WriteLine($"Screenshot saved to {fullPath}");
        }
        else
        {
            Console.Error.WriteLine("Failed to capture rendered frame.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static AppBuilder BuildHeadlessApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            })
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
