using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Import;

namespace Runix.Desktop.Views.Dialogs;

public partial class ImportWizardDialog : UserControl
{
    private IDatabaseService? _databaseService;
    private string _targetTableName = string.Empty;
    private List<ColumnInfo> _targetColumns = new();
    private readonly IDataImportService _importService = DataImportService.Instance;

    private ImportPreviewData? _previewData;
    private ObservableCollection<ColumnMappingItem> _mappingItems = new();
    private CancellationTokenSource? _importCts;
    private int _currentStep = 1;

    public event Action? CancelRequested;
    public event Action<ImportExecutionResult>? ImportCompleted;

    public ImportWizardDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => OnCancelOrClose();
        BtnCancel.Click += (s, e) => OnCancelOrClose();
        BtnBrowseFile.Click += async (s, e) => await OnBrowseFileAsync();
        BtnNext.Click += (s, e) => GoToNextStep();
        BtnBack.Click += (s, e) => GoToPreviousStep();
        BtnStartImport.Click += async (s, e) => await StartImportAsync();
        BtnFinish.Click += (s, e) => OnCancelOrClose();
        BtnAutoMap.Click += (s, e) => ApplyAutoMapping();

        CmbDelimiter.SelectionChanged += async (s, e) =>
        {
            if (_previewData != null && !string.IsNullOrEmpty(TxtFilePath.Text) && CmbDelimiter.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                char? delim = tag == "Auto" || string.IsNullOrEmpty(tag) ? null : tag[0];
                await LoadAndPreviewFileAsync(TxtFilePath.Text, delim);
            }
        };
    }

    public void SetContext(IDatabaseService databaseService, string targetTableName, List<ColumnInfo> targetColumns)
    {
        _databaseService = databaseService;
        _targetTableName = targetTableName;
        _targetColumns = targetColumns ?? new List<ColumnInfo>();

        TxtTargetTableName.Text = targetTableName;
        GoToStep(1);
    }

    public void PreloadFile(string filePath)
    {
        TxtFilePath.Text = filePath;
        _ = LoadAndPreviewFileAsync(filePath);
    }

    private async Task OnBrowseFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar arquivo de dados para importação",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Arquivos Suportados (*.csv, *.tsv, *.json, *.xlsx)")
                {
                    Patterns = new[] { "*.csv", "*.tsv", "*.json", "*.xlsx" }
                },
                new("Arquivos CSV / TSV (*.csv, *.tsv)") { Patterns = new[] { "*.csv", "*.tsv" } },
                new("Arquivos JSON (*.json)") { Patterns = new[] { "*.json" } },
                new("Planilhas Excel (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
                new("Todos os arquivos (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            var selectedPath = files[0].Path.LocalPath;
            TxtFilePath.Text = selectedPath;
            await LoadAndPreviewFileAsync(selectedPath);
        }
    }

    public async Task LoadAndPreviewFileAsync(string filePath, char? delimiter = null)
    {
        try
        {
            TxtStep1Error.IsVisible = false;
            TxtDetectionBadge.Text = "Analisando arquivo...";
            TxtDetectionBadge.Foreground = Brush.Parse("#38bdf8");

            var preview = await _importService.PreviewFileAsync(filePath, delimiterHint: delimiter);
            _previewData = preview;

            // Update badge text
            var delimText = preview.DetectedDelimiter.HasValue
                ? $", Delimitador: '{(preview.DetectedDelimiter == '\t' ? "\\t" : preview.DetectedDelimiter.ToString())}'"
                : "";

            TxtDetectionBadge.Text = $"Formato: {preview.DetectedFormat}{delimText} | Encoding: {preview.DetectedEncoding} | ~{preview.EstimatedTotalRows:N0} linhas estimadas";
            TxtDetectionBadge.Foreground = Brush.Parse("#34d399");

            PanelDelimiterOverride.IsVisible = preview.DetectedFormat == ImportFileFormat.Csv;

            // Populate Preview DataGrid
            GridRawPreview.Columns.Clear();
            for (int i = 0; i < preview.Headers.Count; i++)
            {
                var colIndex = i;
                var colName = preview.Headers[i];
                GridRawPreview.Columns.Add(new DataGridTextColumn
                {
                    Header = colName,
                    Binding = new Binding($"[{colIndex}]"),
                    CanUserSort = false
                });
            }

            GridRawPreview.ItemsSource = preview.PreviewRows;

            // Enable Next button
            BtnNext.IsEnabled = preview.Headers.Count > 0;

            // Build initial mapping list
            BuildInitialMappings();
        }
        catch (Exception ex)
        {
            _previewData = null;
            TxtDetectionBadge.Text = "Erro ao inspecionar arquivo.";
            TxtDetectionBadge.Foreground = Brush.Parse("#f87171");
            TxtStep1Error.Text = $"Erro: {ex.Message}";
            TxtStep1Error.IsVisible = true;
            BtnNext.IsEnabled = false;
        }
    }

    private void BuildInitialMappings()
    {
        if (_previewData == null) return;

        var available = new List<string> { "<Ignorar>" };
        available.AddRange(_targetColumns.Select(c => c.Name));

        _mappingItems = new ObservableCollection<ColumnMappingItem>();

        foreach (var header in _previewData.Headers)
        {
            var item = new ColumnMappingItem
            {
                SourceColumn = header,
                AvailableTargetColumns = available,
                SelectedTargetColumn = "<Ignorar>",
                TargetDataType = "-"
            };

            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ColumnMappingItem.SelectedTargetColumn))
                {
                    UpdateDataTypeForItem(item);
                }
            };

            _mappingItems.Add(item);
        }

        ListColumnMappings.ItemsSource = _mappingItems;
        ApplyAutoMapping();
    }

    private void UpdateDataTypeForItem(ColumnMappingItem item)
    {
        if (item.IsIgnored)
        {
            item.TargetDataType = "-";
        }
        else
        {
            var targetCol = _targetColumns.FirstOrDefault(c => c.Name.Equals(item.SelectedTargetColumn, StringComparison.OrdinalIgnoreCase));
            item.TargetDataType = targetCol?.DataType ?? "TEXT";
        }
    }

    private void ApplyAutoMapping()
    {
        if (_previewData == null || _mappingItems.Count == 0) return;

        var autoMappings = ColumnAutoMapper.AutoMapColumns(_previewData.Headers, _targetColumns);

        for (int i = 0; i < _mappingItems.Count && i < autoMappings.Count; i++)
        {
            var mapped = autoMappings[i];
            var item = _mappingItems[i];

            if (!mapped.IsIgnored && !string.IsNullOrWhiteSpace(mapped.TargetColumn))
            {
                item.SelectedTargetColumn = mapped.TargetColumn;
                item.TargetDataType = mapped.TargetDataType ?? "TEXT";
            }
            else
            {
                item.SelectedTargetColumn = "<Ignorar>";
                item.TargetDataType = "-";
            }
        }
    }

    private void GoToNextStep()
    {
        if (_currentStep == 1)
        {
            if (_previewData == null || _previewData.Headers.Count == 0) return;
            GoToStep(2);
        }
        else if (_currentStep == 2)
        {
            GoToStep(3);
        }
    }

    private void GoToPreviousStep()
    {
        if (_currentStep == 2)
        {
            GoToStep(1);
        }
        else if (_currentStep == 3)
        {
            GoToStep(2);
        }
    }

    public void GoToStep(int step)
    {
        _currentStep = step;

        PanelStep1.IsVisible = step == 1;
        PanelStep2.IsVisible = step == 2;
        PanelStep3.IsVisible = step == 3;

        // Step 1 Ribbon Badge
        BadgeStep1.Background = step == 1 ? Brush.Parse("#008bb9") : Brush.Parse("#1e2d42");
        TxtStep1Label.Foreground = step == 1 ? Brush.Parse("#38bdf8") : Brush.Parse("#8b9cb3");

        // Step 2 Ribbon Badge
        BadgeStep2.Background = step == 2 ? Brush.Parse("#008bb9") : Brush.Parse("#1e2d42");
        TxtStep2Label.Foreground = step == 2 ? Brush.Parse("#38bdf8") : Brush.Parse("#8b9cb3");

        // Step 3 Ribbon Badge
        BadgeStep3.Background = step == 3 ? Brush.Parse("#008bb9") : Brush.Parse("#1e2d42");
        TxtStep3Label.Foreground = step == 3 ? Brush.Parse("#38bdf8") : Brush.Parse("#8b9cb3");

        // Footer buttons
        BtnBack.IsVisible = step > 1 && step < 3;
        BtnNext.IsVisible = step == 1;
        BtnStartImport.IsVisible = step == 2;
        BtnFinish.IsVisible = false;
        BtnCancel.IsVisible = step < 3;
    }

    private async Task StartImportAsync()
    {
        if (_databaseService == null || string.IsNullOrWhiteSpace(TxtFilePath.Text)) return;

        var mappedColumns = _mappingItems.Where(m => !m.IsIgnored).ToList();
        if (mappedColumns.Count == 0)
        {
            TxtStep1Error.Text = "Pelo menos uma coluna precisa estar mapeada para iniciar a importação.";
            TxtStep1Error.IsVisible = true;
            return;
        }

        GoToStep(3);

        BtnCancel.Content = "Cancelar Operação";
        BtnCancel.IsVisible = true;
        _importCts = new CancellationTokenSource();

        var strategy = RbRollbackOnError.IsChecked == true
            ? ImportErrorStrategy.RollbackOnError
            : ImportErrorStrategy.SkipErrorsAndContinue;

        var batchSize = int.TryParse(TxtBatchSize.Text, out var b) ? Math.Max(1, b) : 1000;

        var config = new ImportConfiguration
        {
            FilePath = TxtFilePath.Text,
            TargetTable = _targetTableName,
            ErrorStrategy = strategy,
            BatchSize = batchSize,
            Mappings = _mappingItems.Select(m => new ColumnMapping
            {
                SourceColumn = m.SourceColumn,
                TargetColumn = m.IsIgnored ? null : m.SelectedTargetColumn,
                TargetDataType = m.TargetDataType
            }).ToList()
        };

        var totalEstimated = _previewData?.EstimatedTotalRows ?? 100;
        if (totalEstimated <= 0) totalEstimated = 100;

        var progress = new Progress<int>(inserted =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var pct = Math.Min(100, (int)((double)inserted / totalEstimated * 100));
                ProgressBarImport.Value = pct;
                TxtProgressPercentage.Text = $"{pct}%";
                TxtProgressStatus.Text = $"Importando dados... {inserted:N0} linhas inseridas ({pct}%)";
            });
        });

        TxtProgressStatus.Text = "Iniciando importação de dados...";
        ProgressBarImport.Value = 0;
        TxtProgressPercentage.Text = "0%";
        PanelSummaryCard.IsVisible = false;
        PanelErrorsLog.IsVisible = false;

        try
        {
            var result = await _importService.ExecuteImportAsync(_databaseService, config, progress, _importCts.Token);

            ProgressBarImport.Value = 100;
            TxtProgressPercentage.Text = "100%";

            PanelSummaryCard.IsVisible = true;
            if (result.Success)
            {
                TxtSummaryTitle.Text = "✓ Importação Concluída com Sucesso!";
                TxtSummaryTitle.Foreground = Brush.Parse("#34d399");
                TxtSummaryDetails.Text = $"{result.InsertedRowsCount:N0} linhas inseridas em {result.ElapsedFormatted}. ({result.FailedRowsCount} falhas)";
            }
            else
            {
                TxtSummaryTitle.Text = "⚠ Importação Concluída com Falhas / Revertida";
                TxtSummaryTitle.Foreground = Brush.Parse("#f87171");
                TxtSummaryDetails.Text = $"{result.ErrorMessage ?? "Falha na execução"} ({result.InsertedRowsCount:N0} inseridas, {result.FailedRowsCount:N0} falhas, {result.ElapsedFormatted})";
            }

            if (result.Errors.Count > 0)
            {
                PanelErrorsLog.IsVisible = true;
                ListErrors.ItemsSource = result.Errors;
            }

            BtnCancel.IsVisible = false;
            BtnFinish.IsVisible = true;

            ImportCompleted?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            TxtProgressStatus.Text = "Importação cancelada pelo usuário.";
            TxtSummaryTitle.Text = "Operação Cancelada";
            TxtSummaryTitle.Foreground = Brush.Parse("#f59e0b");
            TxtSummaryDetails.Text = "O processo foi interrompido antes do término.";
            PanelSummaryCard.IsVisible = true;
            BtnCancel.IsVisible = false;
            BtnFinish.IsVisible = true;
        }
        catch (Exception ex)
        {
            TxtProgressStatus.Text = $"Erro crítico: {ex.Message}";
            TxtSummaryTitle.Text = "Erro na Execução";
            TxtSummaryTitle.Foreground = Brush.Parse("#f87171");
            TxtSummaryDetails.Text = ex.Message;
            PanelSummaryCard.IsVisible = true;
            BtnCancel.IsVisible = false;
            BtnFinish.IsVisible = true;
        }
    }

    private void OnCancelOrClose()
    {
        if (_importCts != null && !_importCts.IsCancellationRequested)
        {
            try { _importCts.Cancel(); } catch { }
        }
        CancelRequested?.Invoke();
    }
}

public class ColumnMappingItem : INotifyPropertyChanged
{
    private string? _selectedTargetColumn;
    private string? _targetDataType;

    public string SourceColumn { get; set; } = string.Empty;
    public IReadOnlyList<string> AvailableTargetColumns { get; set; } = new List<string>();

    public string? SelectedTargetColumn
    {
        get => _selectedTargetColumn;
        set
        {
            if (_selectedTargetColumn != value)
            {
                _selectedTargetColumn = value;
                OnPropertyChanged(nameof(SelectedTargetColumn));
                OnPropertyChanged(nameof(IsIgnored));
                OnPropertyChanged(nameof(StatusBadge));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string? TargetDataType
    {
        get => _targetDataType;
        set
        {
            if (_targetDataType != value)
            {
                _targetDataType = value;
                OnPropertyChanged(nameof(TargetDataType));
            }
        }
    }

    public bool IsIgnored => string.IsNullOrWhiteSpace(SelectedTargetColumn) || SelectedTargetColumn == "<Ignorar>";
    public string StatusBadge => IsIgnored ? "Ignorado" : "Mapeado";
    public string StatusColor => IsIgnored ? "#8b9cb3" : "#34d399";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
