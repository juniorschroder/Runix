using System;
using System.IO;
using System.Linq;
using Runix.Core.Models;
using SkiaSharp;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador vetorial nativo para PDF utilizando SkiaSharp (SKDocument.CreatePdf).
/// Emite comandos gráficos vetoriais diretos (linhas, curvas cúbicas, retângulos e texto)
/// garantindo texto selecionável e resolução infinita em zoom.
/// </summary>
public static class SkiaPdfErdExporter
{
    public static byte[] ExportPdf(ErdGraphModel graph, ErdExportOptions? options = null)
    {
        using var ms = new MemoryStream();
        ExportPdf(ms, graph, options);
        return ms.ToArray();
    }

    public static void ExportPdf(Stream outputStream, ErdGraphModel graph, ErdExportOptions? options = null)
    {
        var opt = options ?? new ErdExportOptions();
        var box = DiagramBoundingBox.Calculate(graph, opt.Padding);

        var isDark = opt.Theme == ErdExportTheme.DarkRunix;

        // Paleta SkiaSharp
        var canvasBgColor = SKColor.Parse(isDark ? "#090d14" : "#ffffff");
        var cardBgColor = SKColor.Parse(isDark ? "#141d2b" : "#f8fafc");
        var cardBorderColor = SKColor.Parse(isDark ? "#1e2d42" : "#cbd5e1");
        var headerBgColor = SKColor.Parse(isDark ? "#0e141f" : "#e2e8f0");
        var headerBorderColor = SKColor.Parse(isDark ? "#1e2d42" : "#cbd5e1");
        var titleColor = SKColor.Parse(isDark ? "#f0f6fc" : "#0f172a");
        var colNameColor = SKColor.Parse(isDark ? "#cbd5e1" : "#1e293b");
        var colTypeColor = SKColor.Parse(isDark ? "#64748b" : "#64748b");
        var connectorColor = SKColor.Parse(isDark ? "#008bb9" : "#0284c7");
        var badgeBgColor = SKColor.Parse(isDark ? "#0e141f" : "#f1f5f9");
        var badgeBorderColor = SKColor.Parse(isDark ? "#0064a5" : "#0284c7");
        var badgeTextColor = SKColor.Parse(isDark ? "#38bdf8" : "#0284c7");
        var pkColor = SKColor.Parse(isDark ? "#f59e0b" : "#b45309");
        var fkColor = SKColor.Parse(isDark ? "#38bdf8" : "#0284c7");

        float pageWidth;
        float pageHeight;
        float scale = 1.0f;
        float transX = 0f;
        float transY = 0f;

        if (opt.PageSize == ErdPdfPageSize.FitToDiagram)
        {
            pageWidth = (float)box.Width;
            pageHeight = (float)box.Height;
            transX = -(float)box.MinX;
            transY = -(float)box.MinY;
        }
        else if (opt.PageSize == ErdPdfPageSize.A3Landscape)
        {
            pageWidth = 1191f;
            pageHeight = 842f;
            float printableW = pageWidth - 48f;
            float printableH = pageHeight - 48f;
            scale = Math.Min(printableW / (float)box.Width, printableH / (float)box.Height);
            transX = 24f + ((printableW - ((float)box.Width * scale)) / 2f) - ((float)box.MinX * scale);
            transY = 24f + ((printableH - ((float)box.Height * scale)) / 2f) - ((float)box.MinY * scale);
        }
        else // A4Landscape default
        {
            pageWidth = 842f;
            pageHeight = 595f;
            float printableW = pageWidth - 48f;
            float printableH = pageHeight - 48f;
            scale = Math.Min(printableW / (float)box.Width, printableH / (float)box.Height);
            transX = 24f + ((printableW - ((float)box.Width * scale)) / 2f) - ((float)box.MinX * scale);
            transY = 24f + ((printableH - ((float)box.Height * scale)) / 2f) - ((float)box.MinY * scale);
        }

        using var doc = SKDocument.CreatePdf(outputStream);
        using var canvas = doc.BeginPage(pageWidth, pageHeight);

        // Preencher página com fundo do canvas
        using var pageBgPaint = new SKPaint { Color = canvasBgColor, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, pageWidth, pageHeight, pageBgPaint);

        canvas.Save();
        if (opt.PageSize == ErdPdfPageSize.FitToDiagram)
        {
            canvas.Translate(transX, transY);
        }
        else
        {
            canvas.Translate(transX, transY);
            canvas.Scale(scale, scale);
        }

        // 1. SUBJECT AREAS
        if (opt.IncludeSubjectAreas && graph.SubjectAreas != null && graph.SubjectAreas.Count > 0)
        {
            foreach (var area in graph.SubjectAreas)
            {
                var baseCol = SKColor.Parse(area.ColorHex);
                var areaBg = new SKColor(baseCol.Red, baseCol.Green, baseCol.Blue, (byte)(isDark ? 28 : 20));
                var areaHdr = new SKColor(baseCol.Red, baseCol.Green, baseCol.Blue, (byte)(isDark ? 60 : 45));

                using var areaBgPaint = new SKPaint { Color = areaBg, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var areaBorderPaint = new SKPaint { Color = baseCol, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
                using var areaHdrPaint = new SKPaint { Color = areaHdr, Style = SKPaintStyle.Fill, IsAntialias = true };

                var areaRect = new SKRoundRect(new SKRect((float)area.X, (float)area.Y, (float)(area.X + area.Width), (float)(area.Y + area.Height)), 8f, 8f);
                canvas.DrawRoundRect(areaRect, areaBgPaint);
                canvas.DrawRoundRect(areaRect, areaBorderPaint);

                // Cabeçalho da área
                var hdrRect = new SKRoundRect(new SKRect((float)area.X, (float)area.Y, (float)(area.X + area.Width), (float)(area.Y + 28)), 8f, 8f);
                canvas.DrawRoundRect(hdrRect, areaHdrPaint);

                using var areaTitlePaint = new SKPaint
                {
                    Color = baseCol,
                    TextSize = 11f,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                };
                canvas.DrawText(area.Title, (float)area.X + 12f, (float)area.Y + 18f, areaTitlePaint);

                using var countPaint = new SKPaint
                {
                    Color = baseCol,
                    TextSize = 10f,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Right
                };
                canvas.DrawText($"{area.TableCount} tabelas", (float)(area.X + area.Width - 12), (float)area.Y + 18f, countPaint);
            }
        }

        // 2. CONECTORES BEZIER DAS RELAÇÕES FK
        if (graph.Relationships != null && graph.Relationships.Count > 0)
        {
            using var linePaint = new SKPaint
            {
                Color = connectorColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.0f,
                IsAntialias = true
            };

            foreach (var rel in graph.Relationships)
            {
                var sourceNode = graph.FindNode(rel.SourceTable);
                var targetNode = graph.FindNode(rel.TargetTable);
                if (sourceNode == null || targetNode == null) continue;

                var srcColIdx = Math.Max(0, sourceNode.Columns.FindIndex(c => string.Equals(c.Name, rel.SourceColumn, StringComparison.OrdinalIgnoreCase)));
                var tgtColIdx = Math.Max(0, targetNode.Columns.FindIndex(c => string.Equals(c.Name, rel.TargetColumn, StringComparison.OrdinalIgnoreCase)));

                var srcY = (float)(sourceNode.Y + 40 + (srcColIdx * 25) + 12);
                var tgtY = (float)(targetNode.Y + 40 + (tgtColIdx * 25) + 12);

                float srcX, tgtX, cp1X, cp1Y, cp2X, cp2Y;
                float dx;

                if (sourceNode.X + sourceNode.Width <= targetNode.X)
                {
                    srcX = (float)(sourceNode.X + sourceNode.Width);
                    tgtX = (float)targetNode.X;
                    dx = Math.Max(60f, Math.Abs(tgtX - srcX) * 0.5f);
                    cp1X = srcX + dx;
                    cp1Y = srcY;
                    cp2X = tgtX - dx;
                    cp2Y = tgtY;
                }
                else if (targetNode.X + targetNode.Width <= sourceNode.X)
                {
                    srcX = (float)sourceNode.X;
                    tgtX = (float)(targetNode.X + targetNode.Width);
                    dx = Math.Max(60f, Math.Abs(srcX - tgtX) * 0.5f);
                    cp1X = srcX - dx;
                    cp1Y = srcY;
                    cp2X = tgtX + dx;
                    cp2Y = tgtY;
                }
                else
                {
                    srcX = (float)(sourceNode.X + sourceNode.Width);
                    tgtX = (float)(targetNode.X + targetNode.Width);
                    cp1X = srcX + 80f;
                    cp1Y = srcY;
                    cp2X = tgtX + 80f;
                    cp2Y = tgtY;
                }

                using var path = new SKPath();
                path.MoveTo(srcX, srcY);
                path.CubicTo(cp1X, cp1Y, cp2X, cp2Y, tgtX, tgtY);
                canvas.DrawPath(path, linePaint);

                if (opt.IncludeCardinalityBadges)
                {
                    var srcLabel = rel.Cardinality == ErdCardinality.OneToOne ? "1" : (rel.Cardinality == ErdCardinality.ManyToMany ? "M" : "N");
                    DrawBadgePdf(canvas, srcX, srcY, srcLabel, badgeBgColor, badgeBorderColor, badgeTextColor);
                    DrawBadgePdf(canvas, tgtX, tgtY, "1", badgeBgColor, badgeBorderColor, badgeTextColor);
                }
            }
        }

        // 3. CARTÕES DE TABELA COM COLUNAS
        using var cardBgPaint = new SKPaint { Color = cardBgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var cardBorderPaint = new SKPaint { Color = cardBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        using var hdrBgPaint = new SKPaint { Color = headerBgColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var hdrBorderPaint = new SKPaint { Color = headerBorderColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.0f, IsAntialias = true };

        using var titlePaint = new SKPaint
        {
            Color = titleColor,
            TextSize = 13f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        using var colNamePaint = new SKPaint
        {
            Color = colNameColor,
            TextSize = 11f,
            IsAntialias = true
        };

        using var colTypePaint = new SKPaint
        {
            Color = colTypeColor,
            TextSize = 10f,
            IsAntialias = true,
            TextAlign = SKTextAlign.Right
        };

        using var pkBadgePaint = new SKPaint
        {
            Color = pkColor,
            TextSize = 9f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        using var fkBadgePaint = new SKPaint
        {
            Color = fkColor,
            TextSize = 9f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        foreach (var node in graph.Nodes)
        {
            float cardW = (float)(node.Width > 0 ? node.Width : 250);
            float cardH = (float)(node.Height > 0 ? node.Height : 200);
            float x = (float)node.X;
            float y = (float)node.Y;

            // Fundo do cartão e borda
            var cardRoundRect = new SKRoundRect(new SKRect(x, y, x + cardW, y + cardH), 8f, 8f);
            canvas.DrawRoundRect(cardRoundRect, cardBgPaint);
            canvas.DrawRoundRect(cardRoundRect, cardBorderPaint);

            // Cabeçalho
            var hdrRoundRect = new SKRoundRect(new SKRect(x, y, x + cardW, y + 40f), 8f, 8f);
            canvas.Save();
            canvas.ClipRect(new SKRect(x, y, x + cardW, y + 40f));
            canvas.DrawRoundRect(hdrRoundRect, hdrBgPaint);
            canvas.Restore();
            canvas.DrawLine(x, y + 40f, x + cardW, y + 40f, hdrBorderPaint);

            // Título da tabela
            canvas.DrawText(node.TableName, x + 12f, y + 25f, titlePaint);
            canvas.DrawText($"{node.Columns.Count}", x + cardW - 12f, y + 25f, colTypePaint);

            // Colunas
            for (int i = 0; i < node.Columns.Count; i++)
            {
                var col = node.Columns[i];
                float rowY = y + 40f + (i * 25f) + 17f;

                if (col.IsPrimaryKey)
                {
                    canvas.DrawText("PK", x + 10f, rowY, pkBadgePaint);
                }
                else if (graph.Relationships != null && graph.Relationships.Any(r => string.Equals(r.SourceTable, node.TableName, StringComparison.OrdinalIgnoreCase) && string.Equals(r.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    canvas.DrawText("FK", x + 10f, rowY, fkBadgePaint);
                }

                canvas.DrawText(col.Name, x + 32f, rowY, colNamePaint);
                canvas.DrawText(col.DataType, x + cardW - 12f, rowY, colTypePaint);
            }
        }

        canvas.Restore();
        doc.EndPage();
        doc.Close();
    }

    private static void DrawBadgePdf(SKCanvas canvas, float x, float y, string text, SKColor bg, SKColor border, SKColor textColor)
    {
        using var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var borderPaint = new SKPaint { Color = border, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = 9f,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = SKTypeface.FromFamilyName(null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        canvas.DrawCircle(x, y, 9f, bgPaint);
        canvas.DrawCircle(x, y, 9f, borderPaint);
        canvas.DrawText(text, x, y + 3.5f, textPaint);
    }
}
