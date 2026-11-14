using System.Collections.Generic;
using System.Text;
using Runix.Core.Models;
using Runix.Core.Models.Chart;
using Runix.Infrastructure.Chart;
using Xunit;

namespace Runix.Infrastructure.Tests.Chart;

public class ChartAggregationAndRenderingTests
{
    private QueryResult CreateSampleQueryResult()
    {
        var columns = new List<string> { "category", "sales", "rating" };
        var rows = new List<List<object?>>
        {
            new() { "Eletrônicos", 1500.0, 4.5 },
            new() { "Eletrônicos", 500.0, 4.8 },
            new() { "Livros", 200.0, 4.9 },
            new() { "Livros", 300.0, 4.2 },
            new() { "Vestuário", 800.0, 3.8 },
            new() { "Alimentos", 450.0, 4.0 },
            new() { "Brinquedos", 150.0, 4.1 }
        };

        return QueryResult.CreateSuccess(columns, rows, 1.2);
    }

    [Fact]
    public void Aggregate_CalculatesSumCorrectly()
    {
        var qr = CreateSampleQueryResult();
        var config = new ChartConfiguration
        {
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Sum
        };

        var series = ChartDataAggregator.Aggregate(qr, config);

        Assert.Equal(5, series.Points.Count);
        var eletro = series.Points.Find(p => p.Category == "Eletrônicos");
        Assert.NotNull(eletro);
        Assert.Equal(2000.0, eletro.Value);

        var livros = series.Points.Find(p => p.Category == "Livros");
        Assert.NotNull(livros);
        Assert.Equal(500.0, livros.Value);

        Assert.True(series.TotalSum > 0);
        Assert.True(eletro.Percentage > 0);
    }

    [Fact]
    public void Aggregate_CalculatesAverageAndCount()
    {
        var qr = CreateSampleQueryResult();
        var configAvg = new ChartConfiguration
        {
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Average
        };
        var seriesAvg = ChartDataAggregator.Aggregate(qr, configAvg);

        var eletro = seriesAvg.Points.Find(p => p.Category == "Eletrônicos");
        Assert.NotNull(eletro);
        Assert.Equal(1000.0, eletro.Value); // (1500 + 500) / 2

        var configCount = new ChartConfiguration
        {
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Count
        };
        var seriesCount = ChartDataAggregator.Aggregate(qr, configCount);

        var livros = seriesCount.Points.Find(p => p.Category == "Livros");
        Assert.NotNull(livros);
        Assert.Equal(2.0, livros.Value);
    }

    [Fact]
    public void Aggregate_AppliesTopNWithOthers()
    {
        var qr = CreateSampleQueryResult();
        var config = new ChartConfiguration
        {
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Sum,
            TopN = 2
        };

        var series = ChartDataAggregator.Aggregate(qr, config);

        // Top 2 + "Outros" = 3 items
        Assert.Equal(3, series.Points.Count);
        Assert.Equal("Eletrônicos", series.Points[0].Category);
        Assert.Equal(2000.0, series.Points[0].Value);

        Assert.Equal("Vestuário", series.Points[1].Category);
        Assert.Equal(800.0, series.Points[1].Value);

        Assert.Equal("Outros", series.Points[2].Category);
        Assert.Equal(1100.0, series.Points[2].Value); // 500 (Livros) + 450 (Alimentos) + 150 (Brinquedos)
    }

    [Fact]
    public void HitTest_Rectangle_DetectsPoint()
    {
        var hit = new ChartHitTestResult
        {
            ShapeType = ChartHitShape.Rectangle,
            X = 50,
            Y = 100,
            Width = 40,
            Height = 120
        };

        Assert.True(hit.Contains(60, 150));
        Assert.True(hit.Contains(50, 100));
        Assert.True(hit.Contains(90, 220));
        Assert.False(hit.Contains(45, 150));
        Assert.False(hit.Contains(95, 150));
    }

    [Fact]
    public void HitTest_Circle_DetectsPoint()
    {
        var hit = new ChartHitTestResult
        {
            ShapeType = ChartHitShape.Circle,
            CenterX = 100,
            CenterY = 100,
            Radius = 15
        };

        Assert.True(hit.Contains(100, 100));
        Assert.True(hit.Contains(110, 100));
        Assert.False(hit.Contains(120, 100)); // distance 20 > radius 15
    }

    [Fact]
    public void HitTest_DonutSlice_DetectsAngleAndRadius()
    {
        var hit = new ChartHitTestResult
        {
            ShapeType = ChartHitShape.DonutSlice,
            CenterX = 200,
            CenterY = 200,
            InnerRadius = 50,
            OuterRadius = 100,
            StartAngle = 0,
            SweepAngle = 90 // First quadrant (positive X and positive Y in screen coords)
        };

        // Point at distance 75 at 45 degrees: (200 + 75*cos(45), 200 + 75*sin(45)) ~= (253, 253)
        Assert.True(hit.Contains(253, 253));

        // Inside inner radius hole
        Assert.False(hit.Contains(200, 200));

        // Outside outer radius
        Assert.False(hit.Contains(350, 350));
    }

    [Theory]
    [InlineData(ChartType.BarVertical)]
    [InlineData(ChartType.BarHorizontal)]
    [InlineData(ChartType.LineArea)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Donut)]
    public void Render_ProducesValidBitmapAndHitTests(ChartType chartType)
    {
        var qr = CreateSampleQueryResult();
        var config = new ChartConfiguration
        {
            ChartType = chartType,
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Sum,
            Title = $"Teste {chartType}"
        };

        var series = ChartDataAggregator.Aggregate(qr, config);
        using var result = SkiaChartRenderer.Render(series, config, 600, 400);

        Assert.NotNull(result);
        Assert.NotNull(result.Bitmap);
        Assert.Equal(600, result.Width);
        Assert.Equal(400, result.Height);
        Assert.NotEmpty(result.HitTestResults);

        var pngBytes = result.ToPngByteArray();
        Assert.True(pngBytes.Length > 1000);
    }

    [Fact]
    public void RenderSvg_ProducesWellFormedSvgXml()
    {
        var qr = CreateSampleQueryResult();
        var config = new ChartConfiguration
        {
            ChartType = ChartType.BarVertical,
            XColumn = "category",
            YColumn = "sales",
            Aggregation = AggregationType.Sum,
            Title = "Gráfico Vetorial SVG"
        };

        var series = ChartDataAggregator.Aggregate(qr, config);
        var svgBytes = SkiaChartRenderer.RenderSvg(series, config, 600, 400);

        Assert.NotEmpty(svgBytes);
        var svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("<svg", svgText);
        Assert.Contains("</svg>", svgText);
    }
}
