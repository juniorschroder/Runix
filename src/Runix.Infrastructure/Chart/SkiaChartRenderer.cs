using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Runix.Core.Models.Chart;
using SkiaSharp;

namespace Runix.Infrastructure.Chart;

/// <summary>
/// Motor de renderização 2D nativo via SkiaSharp para gráficos de alto desempenho no tema dark RUNIX.
/// </summary>
public static class SkiaChartRenderer
{
    private static readonly SKColor BgColor = SKColor.Parse("#090d14");
    private static readonly SKColor PanelColor = SKColor.Parse("#0e141f");
    private static readonly SKColor GridLineColor = SKColor.Parse("#162235");
    private static readonly SKColor AxisColor = SKColor.Parse("#2d3f59");
    private static readonly SKColor TextMutedColor = SKColor.Parse("#8b9cb3");
    private static readonly SKColor TextBrightColor = SKColor.Parse("#f0f6fc");

    public static ChartRenderResult Render(ChartSeriesData series, ChartConfiguration config, int width, int height, float scale = 1.0f)
    {
        if (width <= 0) width = 800;
        if (height <= 0) height = 500;

        var pixelWidth = (int)(width * scale);
        var pixelHeight = (int)(height * scale);

        var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(scale, scale);

        var hitTests = DrawChartInternal(canvas, series, config, width, height);

        return new ChartRenderResult(bitmap, hitTests, width, height, scale);
    }

    public static byte[] RenderSvg(ChartSeriesData series, ChartConfiguration config, int width, int height)
    {
        if (width <= 0) width = 800;
        if (height <= 0) height = 500;

        using var ms = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), ms))
        {
            DrawChartInternal(canvas, series, config, width, height);
        }

        return ms.ToArray();
    }

    private static List<ChartHitTestResult> DrawChartInternal(SKCanvas canvas, ChartSeriesData series, ChartConfiguration config, int width, int height)
    {
        var hitTests = new List<ChartHitTestResult>();

        // 1. Draw Background
        using (var bgPaint = new SKPaint { Color = BgColor, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        // 2. Draw Title if present
        float topMargin = 40f;
        var titleText = !string.IsNullOrEmpty(config.Title) ? config.Title : series.SeriesName;
        if (!string.IsNullOrWhiteSpace(titleText))
        {
            topMargin = 55f;
            using var titlePaint = new SKPaint
            {
                Color = TextBrightColor,
                IsAntialias = true,
                TextSize = 16f,
                FakeBoldText = true
            };
            canvas.DrawText(titleText, 25f, 32f, titlePaint);
        }

        if (series.Points.Count == 0)
        {
            using var emptyPaint = new SKPaint
            {
                Color = TextMutedColor,
                IsAntialias = true,
                TextSize = 14f,
                TextAlign = SKTextAlign.Center
            };
            canvas.DrawText("Nenhum dado numérico disponível para plotar.", width / 2f, height / 2f, emptyPaint);
            return hitTests;
        }

        // 3. Define Plot Area
        float bottomMargin = 50f;
        float leftMargin = 65f;
        float rightMargin = config.ShowLegend && config.ChartType != ChartType.Donut ? 130f : 30f;

        if (config.ChartType == ChartType.BarHorizontal)
        {
            leftMargin = 120f;
            rightMargin = 60f;
        }

        float plotLeft = leftMargin;
        float plotTop = topMargin;
        float plotRight = width - rightMargin;
        float plotBottom = height - bottomMargin;
        float plotWidth = plotRight - plotLeft;
        float plotHeight = plotBottom - plotTop;

        // 4. Dispatch based on ChartType
        switch (config.ChartType)
        {
            case ChartType.BarVertical:
                DrawBarVertical(canvas, series, plotLeft, plotTop, plotRight, plotBottom, hitTests);
                break;

            case ChartType.BarHorizontal:
                DrawBarHorizontal(canvas, series, plotLeft, plotTop, plotRight, plotBottom, hitTests);
                break;

            case ChartType.LineArea:
                DrawLineArea(canvas, series, plotLeft, plotTop, plotRight, plotBottom, hitTests);
                break;

            case ChartType.Scatter:
                DrawScatter(canvas, series, plotLeft, plotTop, plotRight, plotBottom, hitTests);
                break;

            case ChartType.Donut:
                DrawDonut(canvas, series, width, height, topMargin, hitTests);
                break;
        }

        // 5. Draw Legend on the right if enabled and space exists
        if (config.ShowLegend && config.ChartType != ChartType.Donut && series.Points.Count <= 15)
        {
            DrawLegend(canvas, series, width - rightMargin + 15f, plotTop, rightMargin - 20f);
        }

        return hitTests;
    }

    private static void DrawBarVertical(SKCanvas canvas, ChartSeriesData series, float left, float top, float right, float bottom, List<ChartHitTestResult> hitTests)
    {
        var points = series.Points;
        double maxVal = Math.Max(0.0001, series.MaxValue * 1.1);
        double minVal = Math.Min(0.0, series.MinValue);
        if (minVal > 0) minVal = 0.0;
        double range = maxVal - minVal;

        // Draw Y Grid & Axes
        DrawYGridAndAxis(canvas, left, top, right, bottom, minVal, maxVal);

        float slotWidth = (right - left) / points.Count;
        float barWidth = Math.Min(50f, Math.Max(6f, slotWidth * 0.65f));

        using var barPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Center };
        using var valPaint = new SKPaint { IsAntialias = true, Color = TextBrightColor, TextSize = 9f, TextAlign = SKTextAlign.Center };

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float cx = left + i * slotWidth + slotWidth / 2f;
            float bx = cx - barWidth / 2f;

            float norm = (float)((p.Value - minVal) / range);
            float barH = norm * (bottom - top);
            float by = bottom - barH;

            barPaint.Color = SKColor.Parse(p.ColorHex);
            var rect = new SKRoundRect(new SKRect(bx, by, bx + barWidth, bottom), 4f, 4f);
            canvas.DrawRoundRect(rect, barPaint);

            // Record HitTest
            hitTests.Add(new ChartHitTestResult
            {
                DataPoint = p,
                ShapeType = ChartHitShape.Rectangle,
                X = bx,
                Y = by,
                Width = barWidth,
                Height = barH
            });

            // Top value badge (if bars aren't too crowded)
            if (points.Count <= 25 && barH > 15)
            {
                canvas.DrawText(p.FormattedValue, cx, by - 4f, valPaint);
            }

            // X Label
            var label = TruncateString(p.Category, Math.Max(4, (int)(slotWidth / 7f)));
            canvas.DrawText(label, cx, bottom + 16f, textPaint);
        }
    }

    private static void DrawBarHorizontal(SKCanvas canvas, ChartSeriesData series, float left, float top, float right, float bottom, List<ChartHitTestResult> hitTests)
    {
        var points = series.Points;
        double maxVal = Math.Max(0.0001, series.MaxValue * 1.1);
        double minVal = 0.0;
        double range = maxVal - minVal;

        // Draw X Grid & Vertical axis
        DrawXGridAndAxis(canvas, left, top, right, bottom, minVal, maxVal);

        float slotHeight = (bottom - top) / points.Count;
        float barHeight = Math.Min(35f, Math.Max(6f, slotHeight * 0.65f));

        using var barPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Right };
        using var valPaint = new SKPaint { IsAntialias = true, Color = TextBrightColor, TextSize = 10f, TextAlign = SKTextAlign.Left };

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float cy = top + i * slotHeight + slotHeight / 2f;
            float by = cy - barHeight / 2f;

            float norm = (float)((p.Value - minVal) / range);
            float barW = norm * (right - left);

            barPaint.Color = SKColor.Parse(p.ColorHex);
            var rect = new SKRoundRect(new SKRect(left, by, left + barW, by + barHeight), 4f, 4f);
            canvas.DrawRoundRect(rect, barPaint);

            hitTests.Add(new ChartHitTestResult
            {
                DataPoint = p,
                ShapeType = ChartHitShape.Rectangle,
                X = left,
                Y = by,
                Width = barW,
                Height = barHeight
            });

            // Category label on left
            var catLabel = TruncateString(p.Category, 14);
            canvas.DrawText(catLabel, left - 10f, cy + 4f, textPaint);

            // Value badge on right
            canvas.DrawText(p.FormattedValue, left + barW + 6f, cy + 4f, valPaint);
        }
    }

    private static void DrawLineArea(SKCanvas canvas, ChartSeriesData series, float left, float top, float right, float bottom, List<ChartHitTestResult> hitTests)
    {
        var points = series.Points;
        double maxVal = Math.Max(0.0001, series.MaxValue * 1.15);
        double minVal = Math.Min(0.0, series.MinValue);
        if (minVal > 0) minVal = 0.0;
        double range = maxVal - minVal;

        DrawYGridAndAxis(canvas, left, top, right, bottom, minVal, maxVal);

        if (points.Count < 2)
        {
            DrawBarVertical(canvas, series, left, top, right, bottom, hitTests);
            return;
        }

        var pts = new List<SKPoint>();
        float stepX = (right - left) / (points.Count - 1);

        for (int i = 0; i < points.Count; i++)
        {
            float px = left + i * stepX;
            float norm = (float)((points[i].Value - minVal) / range);
            float py = bottom - norm * (bottom - top);
            pts.Add(new SKPoint(px, py));
        }

        // Draw Translucent Gradient Fill Area
        using (var areaPath = new SKPath())
        {
            areaPath.MoveTo(pts[0].X, bottom);
            for (int i = 0; i < pts.Count; i++)
            {
                areaPath.LineTo(pts[i]);
            }
            areaPath.LineTo(pts[pts.Count - 1].X, bottom);
            areaPath.Close();

            var mainColor = SKColor.Parse(points[0].ColorHex);
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, top),
                new SKPoint(0, bottom),
                new[] { mainColor.WithAlpha(90), mainColor.WithAlpha(10) },
                null,
                SKShaderTileMode.Clamp);

            using var areaPaint = new SKPaint
            {
                IsAntialias = true,
                Shader = shader,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawPath(areaPath, areaPaint);
        }

        // Draw Line Stroke
        using (var linePath = new SKPath())
        {
            linePath.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++)
            {
                linePath.LineTo(pts[i]);
            }

            using var linePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse(points[0].ColorHex),
                StrokeWidth = 3f,
                Style = SKPaintStyle.Stroke
            };
            canvas.DrawPath(linePath, linePaint);
        }

        // Draw Point Glyphs & HitTests
        using var pointPaint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(points[0].ColorHex), Style = SKPaintStyle.Fill };
        using var pointBorder = new SKPaint { IsAntialias = true, Color = BgColor, StrokeWidth = 2f, Style = SKPaintStyle.Stroke };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Center };

        for (int i = 0; i < points.Count; i++)
        {
            var pt = pts[i];
            canvas.DrawCircle(pt.X, pt.Y, 5f, pointPaint);
            canvas.DrawCircle(pt.X, pt.Y, 5f, pointBorder);

            hitTests.Add(new ChartHitTestResult
            {
                DataPoint = points[i],
                ShapeType = ChartHitShape.Circle,
                CenterX = pt.X,
                CenterY = pt.Y,
                Radius = 10f
            });

            // X Label
            if (points.Count <= 20 || i % (points.Count / 10) == 0)
            {
                var label = TruncateString(points[i].Category, 10);
                canvas.DrawText(label, pt.X, bottom + 16f, textPaint);
            }
        }
    }

    private static void DrawScatter(SKCanvas canvas, ChartSeriesData series, float left, float top, float right, float bottom, List<ChartHitTestResult> hitTests)
    {
        var points = series.Points;
        double maxVal = Math.Max(0.0001, series.MaxValue * 1.15);
        double minVal = Math.Min(0.0, series.MinValue);
        double range = maxVal - minVal;

        DrawYGridAndAxis(canvas, left, top, right, bottom, minVal, maxVal);

        using var ptPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var haloPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        float stepX = points.Count > 1 ? (right - left) / (points.Count - 1) : (right - left) / 2f;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float px = left + i * stepX;
            float norm = (float)((p.Value - minVal) / range);
            float py = bottom - norm * (bottom - top);

            var color = SKColor.Parse(p.ColorHex);
            haloPaint.Color = color.WithAlpha(40);
            ptPaint.Color = color;

            canvas.DrawCircle(px, py, 10f, haloPaint);
            canvas.DrawCircle(px, py, 5f, ptPaint);

            hitTests.Add(new ChartHitTestResult
            {
                DataPoint = p,
                ShapeType = ChartHitShape.Circle,
                CenterX = px,
                CenterY = py,
                Radius = 12f
            });
        }
    }

    private static void DrawDonut(SKCanvas canvas, ChartSeriesData series, float width, float height, float topMargin, List<ChartHitTestResult> hitTests)
    {
        var points = series.Points;
        float plotH = height - topMargin - 20f;
        float plotW = width * 0.65f;
        float cx = 30f + plotW / 2f;
        float cy = topMargin + plotH / 2f;

        float outerR = Math.Min(plotW, plotH) * 0.42f;
        float innerR = outerR * 0.58f;

        float currentAngle = -90f; // Start at top
        using var arcPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            float sweepAngle = (float)((p.Percentage ?? 0.0) / 100.0 * 360.0);
            if (sweepAngle < 0.5f) sweepAngle = 0.5f;

            arcPaint.Color = SKColor.Parse(p.ColorHex);

            using var path = new SKPath();
            var outerRect = new SKRect(cx - outerR, cy - outerR, cx + outerR, cy + outerR);
            var innerRect = new SKRect(cx - innerR, cy - innerR, cx + innerR, cy + innerR);

            path.ArcTo(outerRect, currentAngle, sweepAngle, false);
            path.ArcTo(innerRect, currentAngle + sweepAngle, -sweepAngle, false);
            path.Close();

            canvas.DrawPath(path, arcPaint);

            hitTests.Add(new ChartHitTestResult
            {
                DataPoint = p,
                ShapeType = ChartHitShape.DonutSlice,
                CenterX = cx,
                CenterY = cy,
                InnerRadius = innerR,
                OuterRadius = outerR,
                StartAngle = currentAngle,
                SweepAngle = sweepAngle
            });

            currentAngle += sweepAngle;
        }

        // Center cutout summary
        using var centerBg = new SKPaint { IsAntialias = true, Color = BgColor, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(cx, cy, innerR - 2f, centerBg);

        using var totalLabelPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Center };
        using var totalValPaint = new SKPaint { IsAntialias = true, Color = TextBrightColor, TextSize = 14f, FakeBoldText = true, TextAlign = SKTextAlign.Center };

        canvas.DrawText("TOTAL", cx, cy - 6f, totalLabelPaint);
        var totalStr = FormatNumeric(series.TotalSum);
        canvas.DrawText(totalStr, cx, cy + 12f, totalValPaint);

        // Donut Legend on the Right
        float legendLeft = cx + outerR + 30f;
        float legendTop = cy - outerR;
        DrawLegend(canvas, series, legendLeft, legendTop, width - legendLeft - 20f, showPercentage: true);
    }

    private static void DrawYGridAndAxis(SKCanvas canvas, float left, float top, float right, float bottom, double minVal, double maxVal)
    {
        using var gridPaint = new SKPaint { Color = GridLineColor, StrokeWidth = 1f, Style = SKPaintStyle.Stroke };
        using var axisPaint = new SKPaint { Color = AxisColor, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Right };

        int steps = 5;
        double range = maxVal - minVal;

        for (int i = 0; i <= steps; i++)
        {
            float norm = (float)i / steps;
            float y = bottom - norm * (bottom - top);
            double val = minVal + norm * range;

            // Horizontal Grid line
            canvas.DrawLine(left, y, right, y, gridPaint);

            // Label
            var valStr = FormatNumeric(val);
            canvas.DrawText(valStr, left - 8f, y + 4f, textPaint);
        }

        // Main Axes
        canvas.DrawLine(left, top, left, bottom, axisPaint);
        canvas.DrawLine(left, bottom, right, bottom, axisPaint);
    }

    private static void DrawXGridAndAxis(SKCanvas canvas, float left, float top, float right, float bottom, double minVal, double maxVal)
    {
        using var gridPaint = new SKPaint { Color = GridLineColor, StrokeWidth = 1f, Style = SKPaintStyle.Stroke };
        using var axisPaint = new SKPaint { Color = AxisColor, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Center };

        int steps = 5;
        double range = maxVal - minVal;

        for (int i = 0; i <= steps; i++)
        {
            float norm = (float)i / steps;
            float x = left + norm * (right - left);
            double val = minVal + norm * range;

            canvas.DrawLine(x, top, x, bottom, gridPaint);
            var valStr = FormatNumeric(val);
            canvas.DrawText(valStr, x, bottom + 16f, textPaint);
        }

        canvas.DrawLine(left, top, left, bottom, axisPaint);
        canvas.DrawLine(left, bottom, right, bottom, axisPaint);
    }

    private static void DrawLegend(SKCanvas canvas, ChartSeriesData series, float x, float y, float maxWidth, bool showPercentage = false)
    {
        using var boxPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var textPaint = new SKPaint { IsAntialias = true, Color = TextMutedColor, TextSize = 10f, TextAlign = SKTextAlign.Left };

        float rowH = 20f;
        int maxRows = Math.Min(series.Points.Count, 12);

        for (int i = 0; i < maxRows; i++)
        {
            var p = series.Points[i];
            float ry = y + i * rowH;

            boxPaint.Color = SKColor.Parse(p.ColorHex);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, ry, x + 10f, ry + 10f), 2f, 2f), boxPaint);

            var pctStr = showPercentage && p.Percentage.HasValue ? $" ({p.Percentage.Value:F1}%)" : "";
            var text = $"{TruncateString(p.Category, 14)}{pctStr}";
            canvas.DrawText(text, x + 16f, ry + 9f, textPaint);
        }
    }

    private static string TruncateString(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maxChars) return text;
        return text.Substring(0, Math.Max(1, maxChars - 2)) + "…";
    }

    private static string FormatNumeric(double val)
    {
        if (Math.Abs(val) >= 1_000_000)
            return $"{(val / 1_000_000.0):F1}M";
        if (Math.Abs(val) >= 1_000)
            return $"{(val / 1_000.0):F1}K";
        if (Math.Abs(val % 1) < 0.001)
            return val.ToString("N0", CultureInfo.InvariantCulture);

        return val.ToString("N1", CultureInfo.InvariantCulture);
    }
}
