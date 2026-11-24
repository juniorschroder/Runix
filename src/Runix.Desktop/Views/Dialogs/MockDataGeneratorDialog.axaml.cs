using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.MockData;
using Runix.Infrastructure.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class MockDataGeneratorDialog : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly string _tableName;
    private readonly IMockDataGeneratorService _generatorService;
    private readonly List<ColumnGeneratorRule> _rules = new();
    private CancellationTokenSource? _seedingCts;

    public event Action? CloseRequested;
    public event Action<int>? SeedingCompleted;

    public MockDataGeneratorDialog()
    {
        InitializeComponent();
        _databaseService = null!;
        _tableName = string.Empty;
        _generatorService = new MockDataGeneratorService();
    }

    public MockDataGeneratorDialog(
        IDatabaseService databaseService,
        string tableName,
        IMockDataGeneratorService? generatorService = null)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _tableName = tableName;
        _generatorService = generatorService ?? new MockDataGeneratorService();

        TxtDialect.Text = $"[{_databaseService.Config.Type}]";
        TxtSubtitle.Text = $"Tabela: {_tableName}";

        SetupEvents();
        _ = LoadMetadataAsync();
    }

    private void SetupEvents()
    {
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnCloseFooter.Click += (s, e) => CloseRequested?.Invoke();

        BtnRefreshPreview.Click += (s, e) => UpdatePreviewGrid();

        BtnExportSql.Click += async (s, e) => await ExportSqlScriptAsync();

        BtnSeedDatabase.Click += async (s, e) => await StartSeedingAsync();

        BtnCancelSeeding.Click += (s, e) =>
        {
            _seedingCts?.Cancel();
            BtnCancelSeeding.IsEnabled = false;
            TxtProgressStatus.Text = "Cancelando inserção...";
            TxtProgressStatus.Foreground = Brush.Parse("#fbbf24");
        };

        CmbRowCount.SelectionChanged += (s, e) =>
        {
            UpdateSummaryStatus();
        };

        TabMockMain.SelectionChanged += (s, e) =>
        {
            if (TabMockMain.SelectedItem == TabPreview)
            {
                UpdatePreviewGrid();
            }
        };
    }

    private async Task LoadMetadataAsync()
    {
        try
        {
            TxtSummaryStatus.Text = "Carregando metadados e analisando integridade...";
            var columns = await _databaseService.GetTableColumnsAsync(_tableName);
            var foreignKeys = await _databaseService.GetForeignKeysAsync(_tableName);
            var tableInfo = new TableInfo { Name = _tableName };

            _rules.Clear();
            var inferred = _generatorService.InferRules(tableInfo, columns, foreignKeys);
            _rules.AddRange(inferred);

            // Amostra valores de FK para tabelas relacionadas
            await _generatorService.SampleForeignKeysAsync(_databaseService, _rules);

            RenderColumnRules();
            UpdatePreviewGrid();
            UpdateSummaryStatus();
        }
        catch (Exception ex)
        {
            TxtSummaryStatus.Text = $"Erro ao carregar colunas: {ex.Message}";
            TxtSummaryStatus.Foreground = Brush.Parse("#f87171");
        }
    }

    private void RenderColumnRules()
    {
        PanelColumnRules.Children.Clear();

        foreach (var rule in _rules)
        {
            var card = CreateColumnCard(rule);
            PanelColumnRules.Children.Add(card);
        }
    }

    private Control CreateColumnCard(ColumnGeneratorRule rule)
    {
        var border = new Border
        {
            Background = Brush.Parse("#0e141f"),
            BorderBrush = Brush.Parse("#1e2d42"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10, 8),
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,2*,Auto,3*,Auto,Auto")
        };

        // 1. CheckBox Incluir
        var chkInclude = new CheckBox
        {
            IsChecked = !rule.IsSkipped,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };
        ToolTip.SetTip(chkInclude, "Incluir coluna na geração");
        chkInclude.IsCheckedChanged += (s, e) =>
        {
            rule.IsSkipped = chkInclude.IsChecked != true;
            UpdateSummaryStatus();
        };
        Grid.SetColumn(chkInclude, 0);
        grid.Children.Add(chkInclude);

        // 2. Coluna e Tipo + Badges (PK, FK)
        var spColInfo = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        var txtColName = new TextBlock
        {
            Text = rule.ColumnName,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = Brush.Parse("#f0f6fc"),
            VerticalAlignment = VerticalAlignment.Center
        };
        spColInfo.Children.Add(txtColName);

        var borderType = new Border
        {
            Background = Brush.Parse("#1e2d42"),
            CornerRadius = new Avalonia.CornerRadius(3),
            Padding = new Avalonia.Thickness(4, 1),
            VerticalAlignment = VerticalAlignment.Center
        };
        borderType.Child = new TextBlock
        {
            Text = rule.DataType,
            FontSize = 9,
            Foreground = Brush.Parse("#8b9cb3")
        };
        spColInfo.Children.Add(borderType);

        if (rule.IsPrimaryKey)
        {
            var badgePk = new Border
            {
                Background = Brush.Parse("#854d0e"),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(4, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgePk.Child = new TextBlock
            {
                Text = rule.IsAutoIncrement ? "🔑 PK Auto" : "🔑 PK",
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#fef08a")
            };
            spColInfo.Children.Add(badgePk);
        }

        if (!string.IsNullOrWhiteSpace(rule.ForeignKeyTargetTable))
        {
            var fkColor = rule.ParentTableEmpty ? "#ef4444" : "#10b981";
            var fkText = rule.ParentTableEmpty
                ? $"⚠️ FK {rule.ForeignKeyTargetTable} (Vazia)"
                : $"🔗 FK {rule.ForeignKeyTargetTable} ({rule.SampledValues.Count} chaves)";

            var badgeFk = new Border
            {
                Background = Brush.Parse("#13271f"),
                BorderBrush = Brush.Parse(fkColor),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(4, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeFk.Child = new TextBlock
            {
                Text = fkText,
                FontSize = 9,
                Foreground = Brush.Parse(fkColor)
            };
            spColInfo.Children.Add(badgeFk);
        }

        Grid.SetColumn(spColInfo, 1);
        grid.Children.Add(spColInfo);

        // 3. ComboBox Tipo de Gerador
        var cmbGenerator = new ComboBox
        {
            ItemsSource = Enum.GetValues<GeneratorType>(),
            SelectedItem = rule.SelectedGenerator,
            Background = Brush.Parse("#141d2b"),
            Foreground = Brush.Parse("#f0f6fc"),
            BorderBrush = Brush.Parse("#1e2d42"),
            CornerRadius = new Avalonia.CornerRadius(4),
            FontSize = 11,
            Height = 28,
            Margin = new Avalonia.Thickness(8, 0)
        };
        cmbGenerator.SelectionChanged += (s, e) =>
        {
            if (cmbGenerator.SelectedItem is GeneratorType gt)
            {
                rule.SelectedGenerator = gt;
            }
        };
        Grid.SetColumn(cmbGenerator, 2);
        grid.Children.Add(cmbGenerator);

        // 4. Parâmetros rápidos (Faixa Min/Max ou Choices)
        var spParams = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(6, 0)
        };

        if (rule.SelectedGenerator == GeneratorType.Number || rule.SelectedGenerator == GeneratorType.Decimal)
        {
            var txtMin = new TextBox
            {
                Text = (rule.MinValue ?? 1).ToString(),
                Width = 55,
                FontSize = 10,
                Height = 26,
                Background = Brush.Parse("#141d2b"),
                Foreground = Brush.Parse("#f0f6fc"),
                BorderBrush = Brush.Parse("#1e2d42"),
                Watermark = "Min"
            };
            txtMin.TextChanged += (s, e) =>
            {
                if (long.TryParse(txtMin.Text, out var min)) rule.MinValue = min;
            };

            var txtSep = new TextBlock { Text = "-", Foreground = Brush.Parse("#8b9cb3"), VerticalAlignment = VerticalAlignment.Center };

            var txtMax = new TextBox
            {
                Text = (rule.MaxValue ?? 1000).ToString(),
                Width = 60,
                FontSize = 10,
                Height = 26,
                Background = Brush.Parse("#141d2b"),
                Foreground = Brush.Parse("#f0f6fc"),
                BorderBrush = Brush.Parse("#1e2d42"),
                Watermark = "Max"
            };
            txtMax.TextChanged += (s, e) =>
            {
                if (long.TryParse(txtMax.Text, out var max)) rule.MaxValue = max;
            };

            spParams.Children.Add(txtMin);
            spParams.Children.Add(txtSep);
            spParams.Children.Add(txtMax);
        }
        else if (rule.SelectedGenerator == GeneratorType.ChoiceList)
        {
            var txtChoices = new TextBox
            {
                Text = string.Join(", ", rule.Choices.Count > 0 ? rule.Choices : new List<string> { "A", "B", "C" }),
                Width = 140,
                FontSize = 10,
                Height = 26,
                Background = Brush.Parse("#141d2b"),
                Foreground = Brush.Parse("#f0f6fc"),
                BorderBrush = Brush.Parse("#1e2d42"),
                Watermark = "Opções separadas por vírgula"
            };
            txtChoices.TextChanged += (s, e) =>
            {
                rule.Choices = (txtChoices.Text ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            };
            spParams.Children.Add(txtChoices);
        }
        else
        {
            var txtInfo = new TextBlock
            {
                Text = rule.IsNullable ? "Permite nulos" : "Obrigatório (NOT NULL)",
                FontSize = 10,
                Foreground = rule.IsNullable ? Brush.Parse("#8b9cb3") : Brush.Parse("#38bdf8"),
                VerticalAlignment = VerticalAlignment.Center
            };
            spParams.Children.Add(txtInfo);
        }

        Grid.SetColumn(spParams, 3);
        grid.Children.Add(spParams);

        // 5. Nulabilidade % (apenas para colunas anuláveis)
        if (rule.IsNullable && !rule.IsPrimaryKey)
        {
            var spNull = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(6, 0)
            };

            var txtNullLabel = new TextBlock
            {
                Text = $"Null: {rule.NullPercentage}%",
                FontSize = 10,
                Foreground = Brush.Parse("#8b9cb3"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 55
            };

            var sliderNull = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = rule.NullPercentage,
                Width = 60,
                VerticalAlignment = VerticalAlignment.Center
            };
            sliderNull.ValueChanged += (s, e) =>
            {
                rule.NullPercentage = (int)sliderNull.Value;
                txtNullLabel.Text = $"Null: {rule.NullPercentage}%";
            };

            spNull.Children.Add(txtNullLabel);
            spNull.Children.Add(sliderNull);

            Grid.SetColumn(spNull, 4);
            grid.Children.Add(spNull);
        }

        border.Child = grid;
        return border;
    }

    private void UpdatePreviewGrid()
    {
        var config = BuildCurrentConfig(5);
        var rows = _generatorService.GenerateRows(config, 5);
        var activeRules = config.Rules.Where(r => !r.IsSkipped).ToList();

        DgPreview.Columns.Clear();

        for (int i = 0; i < activeRules.Count; i++)
        {
            var rule = activeRules[i];
            var colIndex = i;
            var col = new DataGridTextColumn
            {
                Header = $"{rule.ColumnName} ({rule.SelectedGenerator})",
                Binding = new Binding($"[{colIndex}]"),
                IsReadOnly = true,
                FontSize = 11
            };
            DgPreview.Columns.Add(col);
        }

        var matrix = new List<List<string>>();
        foreach (var row in rows)
        {
            var rowVals = new List<string>();
            foreach (var rule in activeRules)
            {
                row.TryGetValue(rule.ColumnName, out var val);
                rowVals.Add(val != null ? val.ToString()! : "NULL");
            }
            matrix.Add(rowVals);
        }

        DgPreview.ItemsSource = matrix;
    }

    private int GetSelectedRowCount()
    {
        if (CmbRowCount.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            int.TryParse(tagStr, out var count))
        {
            return count;
        }
        return 50;
    }

    private TableMockConfig BuildCurrentConfig(int? overrideCount = null)
    {
        return new TableMockConfig
        {
            TableName = _tableName,
            RowCount = overrideCount ?? GetSelectedRowCount(),
            Dialect = _databaseService.Config.Type,
            Rules = new List<ColumnGeneratorRule>(_rules)
        };
    }

    private void UpdateSummaryStatus()
    {
        var activeCount = _rules.Count(r => !r.IsSkipped);
        var rowCount = GetSelectedRowCount();
        TxtSummaryStatus.Text = $"{activeCount} de {_rules.Count} colunas ativas • {rowCount:N0} linhas configuradas";
        TxtSummaryStatus.Foreground = Brush.Parse("#8b9cb3");
    }

    private async Task ExportSqlScriptAsync()
    {
        try
        {
            var count = GetSelectedRowCount();
            var config = BuildCurrentConfig(count);
            var rows = _generatorService.GenerateRows(config, count);
            var sql = _generatorService.GenerateSqlScript(config, rows, _databaseService.Config.Type);

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null && topLevel.StorageProvider.CanSave)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Exportar Script SQL de Mock Data",
                    DefaultExtension = "sql",
                    SuggestedFileName = $"{_tableName}_mock_{count}rows.sql"
                });

                if (file != null)
                {
                    await using var stream = await file.OpenWriteAsync();
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(sql);

                    TxtSummaryStatus.Text = $"✓ Script salvo em: {file.Name}";
                    TxtSummaryStatus.Foreground = Brush.Parse("#34d399");
                    return;
                }
            }

            // Fallback: se não selecionar arquivo, copia para o clipboard
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(sql);
                TxtSummaryStatus.Text = $"✓ Script SQL copiado para a área de transferência ({rows.Count} linhas).";
                TxtSummaryStatus.Foreground = Brush.Parse("#34d399");
            }
        }
        catch (Exception ex)
        {
            TxtSummaryStatus.Text = $"Erro na exportação: {ex.Message}";
            TxtSummaryStatus.Foreground = Brush.Parse("#f87171");
        }
    }

    private async Task StartSeedingAsync()
    {
        var count = GetSelectedRowCount();
        var config = BuildCurrentConfig(count);

        _seedingCts = new CancellationTokenSource();
        BtnSeedDatabase.IsEnabled = false;
        BtnExportSql.IsEnabled = false;
        BtnCancelSeeding.IsVisible = true;
        BtnCancelSeeding.IsEnabled = true;

        PbSeeding.IsVisible = true;
        PbSeeding.Value = 0;
        TxtProgressStatus.IsVisible = true;
        TxtProgressStatus.Text = "Iniciando geração e inserção segura...";
        TxtProgressStatus.Foreground = Brush.Parse("#38bdf8");

        var progress = new Progress<MockDataProgress>(p =>
        {
            PbSeeding.Value = p.Percentage;
            TxtProgressStatus.Text = $"Lote {p.CurrentBatch} de {p.TotalBatches} ({p.Percentage}%) — {p.RowsInserted:N0} de {p.TotalRows:N0} linhas inseridas...";
        });

        try
        {
            var result = await _generatorService.SeedBatchAsync(_databaseService, config, progress, _seedingCts.Token);

            if (result.Success)
            {
                TxtProgressStatus.Text = $"✓ Concluído com sucesso! {result.RowsInserted:N0} linhas inseridas em {result.ElapsedMilliseconds} ms.";
                TxtProgressStatus.Foreground = Brush.Parse("#34d399");
                PbSeeding.Value = 100;

                SeedingCompleted?.Invoke(result.RowsInserted);
            }
            else
            {
                TxtProgressStatus.Text = $"Falha na inserção: {result.ErrorMessage}";
                TxtProgressStatus.Foreground = Brush.Parse("#f87171");
            }
        }
        catch (OperationCanceledException)
        {
            TxtProgressStatus.Text = "Inserção cancelada pelo usuário.";
            TxtProgressStatus.Foreground = Brush.Parse("#fbbf24");
        }
        catch (Exception ex)
        {
            TxtProgressStatus.Text = $"Erro inesperado: {ex.Message}";
            TxtProgressStatus.Foreground = Brush.Parse("#f87171");
        }
        finally
        {
            BtnSeedDatabase.IsEnabled = true;
            BtnExportSql.IsEnabled = true;
            BtnCancelSeeding.IsVisible = false;
        }
    }
}
