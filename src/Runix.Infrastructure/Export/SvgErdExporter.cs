using System;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador vetorial SVG nativo que gera arquivos XML limpos, auto-contidos e escaláveis sem perda de resolução.
/// Suporta cálculo de bounding box exata, curvas Bezier cúbicas, badges de cardinalidade,
/// Subject Areas translúcidas e alternância entre Tema Escuro RUNIX e Tema Claro para Impressão.
/// </summary>
public static class SvgErdExporter
{
    public static string ExportSvg(ErdGraphModel graph, ErdExportOptions? options = null)
    {
        var opt = options ?? new ErdExportOptions();
        var box = DiagramBoundingBox.Calculate(graph, opt.Padding);

        var isDark = opt.Theme == ErdExportTheme.DarkRunix;

        // Paleta do tema
        var canvasBg = isDark ? "#090d14" : "#ffffff";
        var cardBg = isDark ? "#141d2b" : "#f8fafc";
        var cardBorder = isDark ? "#1e2d42" : "#cbd5e1";
        var headerBg = isDark ? "#0e141f" : "#e2e8f0";
        var headerBorder = isDark ? "#1e2d42" : "#cbd5e1";
        var titleColor = isDark ? "#f0f6fc" : "#0f172a";
        var colNameColor = isDark ? "#cbd5e1" : "#1e293b";
        var colTypeColor = isDark ? "#64748b" : "#64748b";
        var connectorColor = isDark ? "#008bb9" : "#0284c7";
        var badgeBg = isDark ? "#0e141f" : "#f1f5f9";
        var badgeBorder = isDark ? "#0064a5" : "#0284c7";
        var badgeText = isDark ? "#38bdf8" : "#0284c7";
        var pkColor = isDark ? "#f59e0b" : "#b45309";
        var fkColor = isDark ? "#38bdf8" : "#0284c7";

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
                      $"viewBox=\"{box.MinX.ToString("F1", inv)} {box.MinY.ToString("F1", inv)} {box.Width.ToString("F1", inv)} {box.Height.ToString("F1", inv)}\" " +
                      $"width=\"{box.Width.ToString("F1", inv)}\" height=\"{box.Height.ToString("F1", inv)}\">");

        // CSS Styles
        sb.AppendLine("  <defs>");
        sb.AppendLine("    <style>");
        sb.AppendLine("      .erd-text { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }");
        sb.AppendLine("      .card-title { font-weight: 600; font-size: 13px; }");
        sb.AppendLine("      .col-name { font-size: 11px; }");
        sb.AppendLine("      .col-type { font-size: 10px; }");
        sb.AppendLine("      .badge-text { font-size: 9px; font-weight: bold; }");
        sb.AppendLine("    </style>");
        sb.AppendLine("  </defs>");

        // Fundo do canvas
        sb.AppendLine($"  <rect x=\"{box.MinX.ToString("F1", inv)}\" y=\"{box.MinY.ToString("F1", inv)}\" " +
                      $"width=\"{box.Width.ToString("F1", inv)}\" height=\"{box.Height.ToString("F1", inv)}\" fill=\"{canvasBg}\" />");

        // 1. SUBJECT AREAS (CAMADA DE FUNDO)
        if (opt.IncludeSubjectAreas && graph.SubjectAreas != null && graph.SubjectAreas.Count > 0)
        {
            sb.AppendLine("  <!-- Subject Areas -->");
            sb.AppendLine("  <g id=\"subject-areas\">");
            foreach (var area in graph.SubjectAreas)
            {
                var areaColor = area.ColorHex;
                var areaFill = isDark ? $"{areaColor}18" : $"{areaColor}12";
                var areaHeaderFill = isDark ? $"{areaColor}35" : $"{areaColor}25";

                sb.AppendLine($"    <rect x=\"{area.X.ToString("F1", inv)}\" y=\"{area.Y.ToString("F1", inv)}\" " +
                              $"width=\"{area.Width.ToString("F1", inv)}\" height=\"{area.Height.ToString("F1", inv)}\" " +
                              $"rx=\"8\" ry=\"8\" fill=\"{areaFill}\" stroke=\"{areaColor}\" stroke-width=\"1.5\" />");

                sb.AppendLine($"    <rect x=\"{area.X.ToString("F1", inv)}\" y=\"{area.Y.ToString("F1", inv)}\" " +
                              $"width=\"{area.Width.ToString("F1", inv)}\" height=\"28\" " +
                              $"rx=\"8\" ry=\"8\" fill=\"{areaHeaderFill}\" />");

                sb.AppendLine($"    <text class=\"erd-text\" x=\"{(area.X + 12).ToString("F1", inv)}\" y=\"{(area.Y + 18).ToString("F1", inv)}\" " +
                              $"font-size=\"11\" font-weight=\"bold\" fill=\"{areaColor}\">{Escape(area.Title)}</text>");

                sb.AppendLine($"    <text class=\"erd-text\" x=\"{(area.X + area.Width - 12).ToString("F1", inv)}\" y=\"{(area.Y + 18).ToString("F1", inv)}\" " +
                              $"font-size=\"10\" text-anchor=\"end\" fill=\"{areaColor}\">{area.TableCount} tabelas</text>");
            }
            sb.AppendLine("  </g>");
        }

        // 2. CONECTORES BEZIER DAS RELAÇÕES FK
        if (graph.Relationships != null && graph.Relationships.Count > 0)
        {
            sb.AppendLine("  <!-- Relationships (Bezier Connections) -->");
            sb.AppendLine("  <g id=\"relationships\">");

            foreach (var rel in graph.Relationships)
            {
                var sourceNode = graph.FindNode(rel.SourceTable);
                var targetNode = graph.FindNode(rel.TargetTable);
                if (sourceNode == null || targetNode == null) continue;

                var srcColIdx = Math.Max(0, sourceNode.Columns.FindIndex(c => string.Equals(c.Name, rel.SourceColumn, StringComparison.OrdinalIgnoreCase)));
                var tgtColIdx = Math.Max(0, targetNode.Columns.FindIndex(c => string.Equals(c.Name, rel.TargetColumn, StringComparison.OrdinalIgnoreCase)));

                var srcY = sourceNode.Y + 40 + (srcColIdx * 25) + 12;
                var tgtY = targetNode.Y + 40 + (tgtColIdx * 25) + 12;

                double srcX, tgtX, cp1X, cp1Y, cp2X, cp2Y;
                double dx;

                if (sourceNode.X + sourceNode.Width <= targetNode.X)
                {
                    srcX = sourceNode.X + sourceNode.Width;
                    tgtX = targetNode.X;
                    dx = Math.Max(60, Math.Abs(tgtX - srcX) * 0.5);
                    cp1X = srcX + dx;
                    cp1Y = srcY;
                    cp2X = tgtX - dx;
                    cp2Y = tgtY;
                }
                else if (targetNode.X + targetNode.Width <= sourceNode.X)
                {
                    srcX = sourceNode.X;
                    tgtX = targetNode.X + targetNode.Width;
                    dx = Math.Max(60, Math.Abs(srcX - tgtX) * 0.5);
                    cp1X = srcX - dx;
                    cp1Y = srcY;
                    cp2X = tgtX + dx;
                    cp2Y = tgtY;
                }
                else
                {
                    srcX = sourceNode.X + sourceNode.Width;
                    tgtX = targetNode.X + targetNode.Width;
                    cp1X = srcX + 80;
                    cp1Y = srcY;
                    cp2X = tgtX + 80;
                    cp2Y = tgtY;
                }

                sb.AppendLine($"    <path d=\"M {srcX.ToString("F1", inv)} {srcY.ToString("F1", inv)} " +
                              $"C {cp1X.ToString("F1", inv)} {cp1Y.ToString("F1", inv)}, " +
                              $"{cp2X.ToString("F1", inv)} {cp2Y.ToString("F1", inv)}, " +
                              $"{tgtX.ToString("F1", inv)} {tgtY.ToString("F1", inv)}\" " +
                              $"fill=\"none\" stroke=\"{connectorColor}\" stroke-width=\"2.0\" />");

                if (opt.IncludeCardinalityBadges)
                {
                    var srcLabel = rel.Cardinality == ErdCardinality.OneToOne ? "1" : (rel.Cardinality == ErdCardinality.ManyToMany ? "M" : "N");
                    DrawBadgeSvg(sb, srcX, srcY, srcLabel, badgeBg, badgeBorder, badgeText, inv);
                    DrawBadgeSvg(sb, tgtX, tgtY, "1", badgeBg, badgeBorder, badgeText, inv);
                }
            }
            sb.AppendLine("  </g>");
        }

        // 3. CARTÕES DE TABELA COM COLUNAS
        sb.AppendLine("  <!-- Tables and Columns -->");
        sb.AppendLine("  <g id=\"tables\">");

        foreach (var node in graph.Nodes)
        {
            double cardW = node.Width > 0 ? node.Width : 250;
            double cardH = node.Height > 0 ? node.Height : 200;

            // Fundo do cartão
            sb.AppendLine($"    <rect x=\"{node.X.ToString("F1", inv)}\" y=\"{node.Y.ToString("F1", inv)}\" " +
                          $"width=\"{cardW.ToString("F1", inv)}\" height=\"{cardH.ToString("F1", inv)}\" " +
                          $"rx=\"8\" ry=\"8\" fill=\"{cardBg}\" stroke=\"{cardBorder}\" stroke-width=\"1.5\" />");

            // Cabeçalho da tabela
            sb.AppendLine($"    <path d=\"M {node.X.ToString("F1", inv)} {(node.Y + 8).ToString("F1", inv)} " +
                          $"a 8 8 0 0 1 8 -8 h {(cardW - 16).ToString("F1", inv)} a 8 8 0 0 1 8 8 v 32 h {(-cardW).ToString("F1", inv)} z\" " +
                          $"fill=\"{headerBg}\" />");

            sb.AppendLine($"    <line x1=\"{node.X.ToString("F1", inv)}\" y1=\"{(node.Y + 40).ToString("F1", inv)}\" " +
                          $"x2=\"{(node.X + cardW).ToString("F1", inv)}\" y2=\"{(node.Y + 40).ToString("F1", inv)}\" " +
                          $"stroke=\"{headerBorder}\" stroke-width=\"1\" />");

            // Título da tabela
            sb.AppendLine($"    <text class=\"erd-text card-title\" x=\"{(node.X + 12).ToString("F1", inv)}\" y=\"{(node.Y + 25).ToString("F1", inv)}\" " +
                          $"fill=\"{titleColor}\">{Escape(node.TableName)}</text>");

            // Badge com contagem de colunas
            sb.AppendLine($"    <text class=\"erd-text\" x=\"{(node.X + cardW - 12).ToString("F1", inv)}\" y=\"{(node.Y + 25).ToString("F1", inv)}\" " +
                          $"font-size=\"10\" text-anchor=\"end\" fill=\"{colTypeColor}\">{node.Columns.Count}</text>");

            // Linhas de colunas
            for (int i = 0; i < node.Columns.Count; i++)
            {
                var col = node.Columns[i];
                var rowY = node.Y + 40 + (i * 25) + 17;

                // Indicador PK / FK
                string prefix = "  ";
                string markerColor = colNameColor;
                if (col.IsPrimaryKey)
                {
                    prefix = "PK";
                    markerColor = pkColor;
                }
                else if (graph.Relationships != null && graph.Relationships.Any(r => string.Equals(r.SourceTable, node.TableName, StringComparison.OrdinalIgnoreCase) && string.Equals(r.SourceColumn, col.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    prefix = "FK";
                    markerColor = fkColor;
                }

                if (prefix != "  ")
                {
                    sb.AppendLine($"    <text class=\"erd-text badge-text\" x=\"{(node.X + 10).ToString("F1", inv)}\" y=\"{rowY.ToString("F1", inv)}\" " +
                                  $"fill=\"{markerColor}\">{prefix}</text>");
                }

                // Nome da coluna
                sb.AppendLine($"    <text class=\"erd-text col-name\" x=\"{(node.X + 32).ToString("F1", inv)}\" y=\"{rowY.ToString("F1", inv)}\" " +
                              $"fill=\"{colNameColor}\">{Escape(col.Name)}</text>");

                // Tipo de dado
                sb.AppendLine($"    <text class=\"erd-text col-type\" x=\"{(node.X + cardW - 12).ToString("F1", inv)}\" y=\"{rowY.ToString("F1", inv)}\" " +
                              $"text-anchor=\"end\" fill=\"{colTypeColor}\">{Escape(col.DataType)}</text>");
            }
        }

        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");

        return sb.ToString();
    }

    private static void DrawBadgeSvg(StringBuilder sb, double x, double y, string text, string bg, string border, string textColor, CultureInfo inv)
    {
        sb.AppendLine($"    <circle cx=\"{x.ToString("F1", inv)}\" cy=\"{y.ToString("F1", inv)}\" r=\"9\" fill=\"{bg}\" stroke=\"{border}\" stroke-width=\"1.5\" />");
        sb.AppendLine($"    <text class=\"erd-text\" x=\"{x.ToString("F1", inv)}\" y=\"{(y + 3.5).ToString("F1", inv)}\" " +
                      $"font-size=\"9\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"{textColor}\">{Escape(text)}</text>");
    }

    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return SecurityElement.Escape(text);
    }
}
