using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Database;

namespace Runix.Desktop.Views;

public partial class DatabaseHealthTabControl : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly IDatabaseHealthService _healthService;
    private readonly ObservableCollection<TableStorageMetric> _tableMetrics = new();
    private readonly ObservableCollection<IndexHealthFinding> _indexFindings = new();
    private readonly ObservableCollection<IntegrityCheckIssue> _integrityIssues = new();
    private DatabaseHealthReport? _lastReport;

    public DatabaseHealthTabControl() : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public DatabaseHealthTabControl(IDatabaseService databaseService, IDatabaseHealthService? healthService = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _healthService = healthService ?? new DatabaseHealthService(databaseService);

        ListTableMetrics.ItemsSource = _tableMetrics;
        ListIndexFindings.ItemsSource = _indexFindings;
        ListIntegrityIssues.ItemsSource = _integrityIssues;

        SetupEvents();
        _ = RefreshTelemetryAsync(includeIntegrity: false);
    }

    private void SetupEvents()
    {
        BtnRefreshTelemetry.Click += async (s, e) => await RefreshTelemetryAsync(includeIntegrity: false);

        BtnSubTabTelemetry.Click += (s, e) => SwitchSubTab(showMaintenance: false);
        BtnSubTabMaintenance.Click += (s, e) => SwitchSubTab(showMaintenance: true);

        CmbTargetTable.SelectionChanged += (s, e) =>
        {
            TxtMaintenanceSqlPreview.Text = _healthService.GetMaintenanceSqlPreview(MaintenanceOperationType.Vacuum, GetSelectedTargetTable());
        };

        BtnRunVacuum.Click += async (s, e) => await RunMaintenanceOperationAsync(MaintenanceOperationType.Vacuum);
        BtnRunAnalyze.Click += async (s, e) => await RunMaintenanceOperationAsync(MaintenanceOperationType.Analyze);
        BtnRunVacuumAnalyze.Click += async (s, e) => await RunMaintenanceOperationAsync(MaintenanceOperationType.VacuumAnalyze);
        BtnRunReindex.Click += async (s, e) => await RunMaintenanceOperationAsync(MaintenanceOperationType.Reindex);
        BtnRunIntegrityCheck.Click += async (s, e) => await RunMaintenanceOperationAsync(MaintenanceOperationType.IntegrityCheck);

        BtnCopyMarkdownReport.Click += async (s, e) =>
        {
            if (_lastReport == null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(_lastReport.ToMarkdownReport());
                TxtStatusSummary.Text = "✓ Relatório Markdown copiado para a área de transferência!";
            }
        };

        BtnCopyJsonReport.Click += async (s, e) =>
        {
            if (_lastReport == null) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(_lastReport.ToJsonReport());
                TxtStatusSummary.Text = "✓ Diagnóstico JSON copiado para a área de transferência!";
            }
        };
    }

    private void SwitchSubTab(bool showMaintenance)
    {
        PanelTelemetryView.IsVisible = !showMaintenance;
        PanelMaintenanceView.IsVisible = showMaintenance;

        BtnSubTabTelemetry.Classes.Clear();
        BtnSubTabTelemetry.Classes.Add(!showMaintenance ? "btn-secondary" : "btn-outline");

        BtnSubTabMaintenance.Classes.Clear();
        BtnSubTabMaintenance.Classes.Add(showMaintenance ? "btn-secondary" : "btn-outline");
    }

    private string? GetSelectedTargetTable()
    {
        if (CmbTargetTable.SelectedIndex <= 0 || CmbTargetTable.SelectedItem is not string tbl)
        {
            return null;
        }
        return tbl;
    }

    public async Task RefreshTelemetryAsync(bool includeIntegrity)
    {
        try
        {
            BtnRefreshTelemetry.IsEnabled = false;
            TxtStatusSummary.Text = "Coletando métricas de armazenamento, páginas e índices...";

            var report = await _healthService.CollectHealthReportAsync(includeIntegrity);
            _lastReport = report;

            ApplyReportToUi(report);
        }
        catch (Exception ex)
        {
            TxtStatusSummary.Text = $"Erro ao coletar telemetria: {ex.Message}";
        }
        finally
        {
            BtnRefreshTelemetry.IsEnabled = true;
        }
    }

    private void ApplyReportToUi(DatabaseHealthReport report)
    {
        TxtStatusSummary.Text = report.StatusSummary;
        TxtFooterTimestamp.Text = $"Última coleta: {report.CollectedAtUtc.ToLocalTime():HH:mm:ss}";
        TxtFooterEngine.Text = $"{report.DatabaseType} • {report.TargetName}";

        switch (report.OverallSeverity)
        {
            case HealthSeverity.Healthy:
                BadgeOverallSeverity.Background = Brush.Parse("#123524");
                BadgeOverallSeverity.BorderBrush = Brush.Parse("#34d399");
                TxtOverallSeverity.Foreground = Brush.Parse("#34d399");
                TxtOverallSeverity.Text = "SAUDÁVEL";
                break;
            case HealthSeverity.Warning:
                BadgeOverallSeverity.Background = Brush.Parse("#2e1f0e");
                BadgeOverallSeverity.BorderBrush = Brush.Parse("#fbbf24");
                TxtOverallSeverity.Foreground = Brush.Parse("#fbbf24");
                TxtOverallSeverity.Text = "ATENÇÃO";
                break;
            case HealthSeverity.Critical:
                BadgeOverallSeverity.Background = Brush.Parse("#2e1419");
                BadgeOverallSeverity.BorderBrush = Brush.Parse("#f87171");
                TxtOverallSeverity.Foreground = Brush.Parse("#f87171");
                TxtOverallSeverity.Text = "CRÍTICO";
                break;
        }

        // KPIs
        TxtKpiTotalSize.Text = report.TotalSizeFormatted;
        TxtKpiSizeBreakdown.Text = $"Dados: {report.DataSizeFormatted} • Índices: {report.IndexSizeFormatted}";

        TxtKpiEfficiency.Text = $"{report.CacheOrPageEfficiencyPercent:F1}%";
        TxtKpiJournalMode.Text = $"Modo {report.JournalOrWalMode} • Page Size {report.PageSize} B";

        TxtKpiFragmentation.Text = $"{report.FragmentationOrDeadTuplePercent:F1}%";
        TxtKpiReclaimable.Text = $"Recuperável via VACUUM: {report.FreeSizeFormatted}";

        TxtKpiCatalogCounts.Text = $"{report.TableCount} Tabelas • {report.IndexCount} Índices";
        TxtKpiConnections.Text = $"{report.ViewCount} Views • {report.ActiveConnections} conexão(ões) ativa(s)";

        // Table Storage List & Target ComboBox
        var scopeItems = new List<string> { "(Banco Inteiro)" };
        _tableMetrics.Clear();
        foreach (var m in report.TableMetrics)
        {
            _tableMetrics.Add(m);
            scopeItems.Add(m.TableName);
        }

        CmbTargetTable.ItemsSource = scopeItems;
        CmbTargetTable.SelectedIndex = 0;

        // Build QueryResult for SkiaChartControl (Top Tables Storage KB)
        var chartCols = new List<string> { "Tabela", "Tamanho_KB", "Linhas" };
        var chartRows = new List<List<object?>>();
        foreach (var m in report.TableMetrics.Take(12))
        {
            double sizeKb = Math.Round(m.TotalSizeBytes / 1024.0, 2);
            chartRows.Add(new List<object?> { m.TableName, sizeKb, m.RowCount });
        }

        if (chartRows.Count > 0)
        {
            var qr = QueryResult.CreateSuccess(chartCols, chartRows, 0);
            ViewStorageChart.SetData(qr, "Ocupação de Armazenamento por Tabela (KB)");
        }

        // Index Findings
        _indexFindings.Clear();
        foreach (var f in report.IndexFindings)
        {
            _indexFindings.Add(f);
        }
        TxtIndexFindingsHeader.Text = $"📇 Diagnóstico de Saúde de Índices ({_indexFindings.Count} alerta(s))";

        // Integrity Issues
        _integrityIssues.Clear();
        if (report.IntegrityIssues.Count == 0)
        {
            _integrityIssues.Add(new IntegrityCheckIssue
            {
                CheckType = "Status",
                Description = "✓ Nenhuma inconsistência reportada na última verificação."
            });
        }
        else
        {
            foreach (var issue in report.IntegrityIssues)
            {
                _integrityIssues.Add(issue);
            }
        }
    }

    private async Task RunMaintenanceOperationAsync(MaintenanceOperationType op)
    {
        string? targetTable = GetSelectedTargetTable();
        TxtMaintenanceSqlPreview.Text = _healthService.GetMaintenanceSqlPreview(op, targetTable);
        TxtMaintenanceFeedback.Text = $"⏳ Executando {op}...";
        TxtMaintenanceFeedback.Foreground = Brush.Parse("#38bdf8");

        var result = await _healthService.ExecuteMaintenanceAsync(op, targetTable);
        TxtMaintenanceFeedback.Text = result.Message;
        TxtMaintenanceFeedback.Foreground = result.Success ? Brush.Parse("#34d399") : Brush.Parse("#f87171");

        if (op == MaintenanceOperationType.IntegrityCheck)
        {
            _integrityIssues.Clear();
            if (result.IntegrityIssues.Count == 0)
            {
                _integrityIssues.Add(new IntegrityCheckIssue
                {
                    CheckType = "PRAGMA integrity_check & foreign_key_check",
                    Description = "✓ 100% Íntegro — Nenhuma corrupção de página ou violação de Foreign Key encontrada."
                });
            }
            else
            {
                foreach (var issue in result.IntegrityIssues)
                {
                    _integrityIssues.Add(issue);
                }
            }
        }

        await RefreshTelemetryAsync(includeIntegrity: false);
    }
}
