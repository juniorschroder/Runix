using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Export;

namespace Runix.Desktop.Views.Dialogs;

public partial class ExportDialog : UserControl
{
    private IDatabaseService? _databaseService;
    private IReadOnlyList<string> _columns = new List<string>();
    private IEnumerable<IReadOnlyList<object?>> _rows = new List<IReadOnlyList<object?>>();
    private string _querySql = string.Empty;
    private string _tableName = "export";
    private CancellationTokenSource? _exportCts;

    public event Action? CancelRequested;
    public event Action<ExportResult>? ExportCompleted;

    public ExportDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => OnCancelClicked();
        BtnCancel.Click += (s, e) => OnCancelClicked();
        BtnExport.Click += async (s, e) => await OnExportClickedAsync();

        RbFormatCsv.IsCheckedChanged += (s, e) => UpdateFormatPanels();
        RbFormatJson.IsCheckedChanged += (s, e) => UpdateFormatPanels();
        RbFormatExcel.IsCheckedChanged += (s, e) => UpdateFormatPanels();
        RbFormatSql.IsCheckedChanged += (s, e) => UpdateFormatPanels();
    }

    public void SetupForQuery(string querySql, QueryResult? currentResult, IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        _querySql = querySql;
        _tableName = "query_export";
        TxtSheetName.Text = "QueryResults";

        if (currentResult != null)
        {
            _columns = currentResult.Columns;
            _rows = currentResult.Rows;
        }

        UpdateFormatPanels();
    }

    public void SetupForTable(string tableName, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows, IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        _tableName = tableName;
        _querySql = $"SELECT * FROM \"{tableName}\";";
        _columns = columns;
        _rows = rows;
        TxtSheetName.Text = tableName;

        UpdateFormatPanels();
    }

    private void UpdateFormatPanels()
    {
        PanelCsvOptions.IsVisible = RbFormatCsv.IsChecked == true;
        PanelJsonOptions.IsVisible = RbFormatJson.IsChecked == true;
        PanelExcelOptions.IsVisible = RbFormatExcel.IsChecked == true;
        PanelSqlOptions.IsVisible = RbFormatSql.IsChecked == true;
    }

    private void OnCancelClicked()
    {
        if (_exportCts != null && !_exportCts.IsCancellationRequested)
        {
            _exportCts.Cancel();
        }
        CancelRequested?.Invoke();
    }

    private async Task OnExportClickedAsync()
    {
        var options = BuildOptions();

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            ShowValidation("Não foi possível acessar a janela principal para seleção de arquivo.");
            return;
        }

        var (ext, desc) = GetExtensionAndDescription(options.Format);
        var suggestedName = $"{_tableName}_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Salvar exportação ({desc})",
            DefaultExtension = ext,
            SuggestedFileName = suggestedName
        });

        if (file == null) return;

        options.FilePath = file.Path.LocalPath;

        // UI progress state
        BtnExport.IsEnabled = false;
        BtnCancel.Content = "Interromper";
        ProgressExport.IsVisible = true;
        ProgressExport.IsIndeterminate = true;
        TxtProgressStatus.Text = "Iniciando exportação...";
        TxtProgressStatus.IsVisible = true;
        PanelValidation.IsVisible = false;

        _exportCts = new CancellationTokenSource();
        var progress = new Progress<int>(rows =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TxtProgressStatus.Text = $"Exportadas {rows:N0} linhas...";
            });
        });

        try
        {
            IReadOnlyList<string> colsToExport = _columns;
            IEnumerable<IReadOnlyList<object?>> rowsToExport = _rows;

            if (options.Scope == ExportScope.FullQueryOrTable && _databaseService != null && !string.IsNullOrWhiteSpace(_querySql))
            {
                TxtProgressStatus.Text = "Carregando conjunto completo do banco de dados...";
                var fullResult = await _databaseService.ExecuteQueryAsync(_querySql, limit: 100000);
                if (fullResult.Success)
                {
                    colsToExport = fullResult.Columns;
                    rowsToExport = fullResult.Rows;
                }
                else
                {
                    ShowValidation($"Falha ao obter dados completos: {fullResult.ErrorMessage}");
                    ResetUiState();
                    return;
                }
            }

            TxtProgressStatus.Text = "Gravando arquivo...";
            var result = await DataExportService.Instance.ExportDataAsync(
                colsToExport, 
                rowsToExport, 
                options, 
                progress, 
                _exportCts.Token);

            ResetUiState();

            if (result.Success)
            {
                ExportCompleted?.Invoke(result);
            }
            else
            {
                ShowValidation(result.ErrorMessage ?? "Erro desconhecido durante a exportação.");
            }
        }
        catch (Exception ex)
        {
            ResetUiState();
            ShowValidation($"Erro inesperado: {ex.Message}");
        }
    }

    private void ResetUiState()
    {
        BtnExport.IsEnabled = true;
        BtnCancel.Content = "Cancelar";
        ProgressExport.IsVisible = false;
        TxtProgressStatus.IsVisible = false;
        _exportCts?.Dispose();
        _exportCts = null;
    }

    private ExportOptions BuildOptions()
    {
        var options = new ExportOptions
        {
            TableName = _tableName,
            TargetDatabaseType = _databaseService?.Config.Type ?? DatabaseType.SQLite,
            Scope = RbScopeFull.IsChecked == true ? ExportScope.FullQueryOrTable : ExportScope.CurrentPage
        };

        if (RbFormatCsv.IsChecked == true)
        {
            options.Format = ExportFormat.Csv;
            options.IncludeHeader = ChkCsvHeader.IsChecked ?? true;
            options.UseBom = ChkCsvBom.IsChecked ?? false;

            if (CmbDelimiter.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                options.CsvDelimiter = tag switch
                {
                    "Semicolon" => CsvDelimiter.Semicolon,
                    "Tab" => CsvDelimiter.Tab,
                    "Pipe" => CsvDelimiter.Pipe,
                    _ => CsvDelimiter.Comma
                };
            }
        }
        else if (RbFormatJson.IsChecked == true)
        {
            options.Format = ExportFormat.Json;
            options.JsonStyle = RbJsonTabular.IsChecked == true ? JsonFormatStyle.Tabular : JsonFormatStyle.ArrayOfObjects;
            options.JsonIndented = ChkJsonIndented.IsChecked ?? true;
        }
        else if (RbFormatExcel.IsChecked == true)
        {
            options.Format = ExportFormat.Excel;
            options.SheetName = string.IsNullOrWhiteSpace(TxtSheetName.Text) ? "Data" : TxtSheetName.Text.Trim();
        }
        else if (RbFormatSql.IsChecked == true)
        {
            options.Format = ExportFormat.SqlDump;
            options.IncludeCreateTable = ChkSqlCreateTable.IsChecked ?? false;
            options.IncludeTransaction = ChkSqlTransaction.IsChecked ?? true;
            options.BatchSize = int.TryParse(TxtSqlBatchSize.Text, out var b) ? Math.Max(1, b) : 500;
        }

        return options;
    }

    private static (string Ext, string Desc) GetExtensionAndDescription(ExportFormat format) => format switch
    {
        ExportFormat.Csv => ("csv", "Arquivo CSV (*.csv)"),
        ExportFormat.Json => ("json", "Arquivo JSON (*.json)"),
        ExportFormat.Excel => ("xlsx", "Planilha Excel (*.xlsx)"),
        ExportFormat.SqlDump => ("sql", "Script SQL Dump (*.sql)"),
        _ => ("txt", "Arquivo de Texto (*.txt)")
    };

    private void ShowValidation(string message)
    {
        TxtValidationMessage.Text = message;
        PanelValidation.IsVisible = true;
    }
}
