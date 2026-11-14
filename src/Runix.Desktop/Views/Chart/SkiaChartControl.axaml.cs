using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Runix.Core.Models;
using Runix.Core.Models.Chart;
using Runix.Infrastructure.Chart;

namespace Runix.Desktop.Views.Chart;

public partial class SkiaChartControl : UserControl
{
    private QueryResult? _currentQueryResult;
    private ChartRenderResult? _lastRenderResult;
    private ChartSeriesData? _lastSeriesData;
    private ChartConfiguration _currentConfig = new();
    private string? _chartTitle;
    private bool _isUpdatingControls;

    public SkiaChartControl()
    {
        InitializeComponent();

        PopulateStaticComboBoxes();
        SetupEvents();
    }

    private void PopulateStaticComboBoxes()
    {
        _isUpdatingControls = true;

        // Chart Types
        CboChartType.Items.Clear();
        CboChartType.Items.Add(new ComboBoxItem { Content = "📊 Barras Verticais", Tag = ChartType.BarVertical });
        CboChartType.Items.Add(new ComboBoxItem { Content = "📋 Barras Horizontais", Tag = ChartType.BarHorizontal });
        CboChartType.Items.Add(new ComboBoxItem { Content = "📈 Linhas & Área", Tag = ChartType.LineArea });
        CboChartType.Items.Add(new ComboBoxItem { Content = "✨ Dispersão (Scatter)", Tag = ChartType.Scatter });
        CboChartType.Items.Add(new ComboBoxItem { Content = "🍩 Donut / Pizza", Tag = ChartType.Donut });
        CboChartType.SelectedIndex = 0;

        // Aggregations
        CboAggregation.Items.Clear();
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Nenhuma (Bruto)", Tag = AggregationType.None });
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Soma (SUM)", Tag = AggregationType.Sum });
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Média (AVG)", Tag = AggregationType.Average });
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Contagem (COUNT)", Tag = AggregationType.Count });
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Mínimo (MIN)", Tag = AggregationType.Min });
        CboAggregation.Items.Add(new ComboBoxItem { Content = "Máximo (MAX)", Tag = AggregationType.Max });
        CboAggregation.SelectedIndex = 1; // Default to SUM

        // Top N limits
        CboTopN.Items.Clear();
        CboTopN.Items.Add(new ComboBoxItem { Content = "Todos", Tag = (int?)null });
        CboTopN.Items.Add(new ComboBoxItem { Content = "Top 10", Tag = (int?)10 });
        CboTopN.Items.Add(new ComboBoxItem { Content = "Top 20", Tag = (int?)20 });
        CboTopN.Items.Add(new ComboBoxItem { Content = "Top 50", Tag = (int?)50 });
        CboTopN.SelectedIndex = 0;

        _isUpdatingControls = false;
    }

    private void SetupEvents()
    {
        CboChartType.SelectionChanged += (s, e) => OnConfigChanged();
        CboXColumn.SelectionChanged += (s, e) => OnConfigChanged();
        CboYColumn.SelectionChanged += (s, e) => OnConfigChanged();
        CboAggregation.SelectionChanged += (s, e) => OnConfigChanged();
        CboTopN.SelectionChanged += (s, e) => OnConfigChanged();

        BtnExportPng.Click += async (s, e) => await ExportPngAsync();
        BtnExportSvg.Click += async (s, e) => await ExportSvgAsync();
        BtnCopyImage.Click += async (s, e) => await CopyImageAsync();

        ImgChartCanvas.PointerMoved += OnCanvasPointerMoved;
        ImgChartCanvas.PointerExited += (s, e) => PanelTooltip.IsVisible = false;

        SizeChanged += (s, e) =>
        {
            if (Bounds.Width > 50 && Bounds.Height > 50)
            {
                RefreshChart();
            }
        };
    }

    public void SetData(QueryResult queryResult, string? title = null)
    {
        _currentQueryResult = queryResult;
        _chartTitle = title;

        _isUpdatingControls = true;

        CboXColumn.Items.Clear();
        CboYColumn.Items.Clear();

        if (queryResult.Columns.Count > 0)
        {
            foreach (var col in queryResult.Columns)
            {
                CboXColumn.Items.Add(new ComboBoxItem { Content = col, Tag = col });
                CboYColumn.Items.Add(new ComboBoxItem { Content = col, Tag = col });
            }

            // Smart Auto-detection
            CboXColumn.SelectedIndex = 0;

            int numericColIdx = ChartDataAggregator.DetectFirstNumericColumn(queryResult, excludeIdx: 0);
            if (numericColIdx >= 0 && numericColIdx < CboYColumn.Items.Count)
            {
                CboYColumn.SelectedIndex = numericColIdx;
            }
            else if (CboYColumn.Items.Count > 1)
            {
                CboYColumn.SelectedIndex = 1;
            }
            else
            {
                CboYColumn.SelectedIndex = 0;
            }
        }

        _isUpdatingControls = false;

        RefreshChart();
    }

    private void OnConfigChanged()
    {
        if (_isUpdatingControls) return;
        RefreshChart();
    }

    public void RefreshChart()
    {
        if (_currentQueryResult == null || _currentQueryResult.Rows.Count == 0 || _currentQueryResult.Columns.Count == 0)
        {
            PanelEmptyState.IsVisible = true;
            ImgChartCanvas.Source = null;
            return;
        }

        PanelEmptyState.IsVisible = false;

        // Build Configuration
        var chartType = ChartType.BarVertical;
        if (CboChartType.SelectedItem is ComboBoxItem itemType && itemType.Tag is ChartType ct)
            chartType = ct;

        var xCol = string.Empty;
        if (CboXColumn.SelectedItem is ComboBoxItem itemX && itemX.Tag is string xc)
            xCol = xc;

        var yCol = string.Empty;
        if (CboYColumn.SelectedItem is ComboBoxItem itemY && itemY.Tag is string yc)
            yCol = yc;

        var agg = AggregationType.Sum;
        if (CboAggregation.SelectedItem is ComboBoxItem itemAgg && itemAgg.Tag is AggregationType at)
            agg = at;

        int? topN = null;
        if (CboTopN.SelectedItem is ComboBoxItem itemTop && itemTop.Tag is int tn)
            topN = tn;

        _currentConfig = new ChartConfiguration
        {
            ChartType = chartType,
            XColumn = xCol,
            YColumn = yCol,
            Aggregation = agg,
            TopN = topN,
            Title = _chartTitle ?? string.Empty
        };

        // Aggregate
        _lastSeriesData = ChartDataAggregator.Aggregate(_currentQueryResult, _currentConfig);

        // Dimensions
        int renderWidth = Math.Max(700, (int)Bounds.Width);
        int renderHeight = Math.Max(420, (int)Bounds.Height - 50);

        _lastRenderResult?.Dispose();
        _lastRenderResult = SkiaChartRenderer.Render(_lastSeriesData, _currentConfig, renderWidth, renderHeight, scale: 2.0f);

        // Display on Image control
        var pngBytes = _lastRenderResult.ToPngByteArray();
        using var stream = new MemoryStream(pngBytes);
        ImgChartCanvas.Source = new Bitmap(stream);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_lastRenderResult == null || _lastRenderResult.HitTestResults.Count == 0)
        {
            PanelTooltip.IsVisible = false;
            return;
        }

        var pt = e.GetPosition(ImgChartCanvas);
        var hit = _lastRenderResult.HitTestResults.Find(h => h.Contains((float)pt.X, (float)pt.Y));

        if (hit != null)
        {
            TxtTooltipCategory.Text = hit.DataPoint.Category;
            TxtTooltipValue.Text = hit.DataPoint.FormattedValue;

            if (hit.DataPoint.Percentage.HasValue && hit.DataPoint.Percentage.Value > 0)
            {
                TxtTooltipPercentage.Text = $"({hit.DataPoint.Percentage.Value:F1}%)";
                TxtTooltipPercentage.IsVisible = true;
            }
            else
            {
                TxtTooltipPercentage.IsVisible = false;
            }

            Canvas.SetLeft(PanelTooltip, pt.X + 16);
            Canvas.SetTop(PanelTooltip, Math.Max(10, pt.Y - 20));
            PanelTooltip.IsVisible = true;
        }
        else
        {
            PanelTooltip.IsVisible = false;
        }
    }

    private async Task ExportPngAsync()
    {
        if (_lastRenderResult == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Gráfico em PNG",
            DefaultExtension = "png",
            SuggestedFileName = $"chart_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            var bytes = _lastRenderResult.ToPngByteArray();
            await stream.WriteAsync(bytes);
        }
    }

    private async Task ExportSvgAsync()
    {
        if (_lastSeriesData == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Gráfico Vetorial em SVG",
            DefaultExtension = "svg",
            SuggestedFileName = $"chart_{DateTime.Now:yyyyMMdd_HHmmss}.svg"
        });

        if (file != null)
        {
            int renderWidth = Math.Max(700, (int)Bounds.Width);
            int renderHeight = Math.Max(420, (int)Bounds.Height - 50);
            var svgBytes = SkiaChartRenderer.RenderSvg(_lastSeriesData, _currentConfig, renderWidth, renderHeight);

            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(svgBytes);
        }
    }

    private async Task CopyImageAsync()
    {
        if (_lastRenderResult == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        try
        {
            var pngBytes = _lastRenderResult.ToPngByteArray();
            var tempFile = Path.Combine(Path.GetTempPath(), $"chart_{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tempFile, pngBytes);

            var dataObj = new DataObject();
            dataObj.Set(DataFormats.Files, new[] { tempFile });
            await topLevel.Clipboard.SetDataObjectAsync(dataObj);

            BtnCopyImage.Content = "✓ Copiado!";
        }
        catch
        {
            // Fallback
            BtnCopyImage.Content = "✓ Pronto";
        }
    }

    /// <summary>
    /// Popula dados realistas para verificação em testes e screenshot headless.
    /// </summary>
    public void EmulateChartForTesting()
    {
        var columns = new List<string> { "categoria_queijo", "vendas_mensais", "avaliacao_media" };
        var rows = new List<List<object?>>
        {
            new() { "Parmesão Curado", 148500.0, 4.9 },
            new() { "Brie Francês", 112300.0, 4.8 },
            new() { "Gouda Holandês", 94200.0, 4.6 },
            new() { "Gorgonzola Cremoso", 78600.0, 4.7 },
            new() { "Cheddar Envelhecido", 65400.0, 4.5 },
            new() { "Provolone Defumado", 43200.0, 4.3 },
            new() { "Emmental Suíço", 36100.0, 4.4 }
        };

        var qr = QueryResult.CreateSuccess(columns, rows, 2.5);
        SetData(qr, "Vendas Mensais por Categoria de Queijo (R$)");

        // Set to BarVertical for high-impact visual presentation
        CboChartType.SelectedIndex = 0;
        RefreshChart();
    }
}
