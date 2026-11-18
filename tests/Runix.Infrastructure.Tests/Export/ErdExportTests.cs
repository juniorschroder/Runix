using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Runix.Core.Models;
using Runix.Infrastructure.Export;
using Xunit;

namespace Runix.Infrastructure.Tests.Export;

public class ErdExportTests
{
    private static ErdGraphModel CreateSampleGraph()
    {
        var graph = new ErdGraphModel();

        var users = new ErdTableNode
        {
            Table = new TableInfo { Name = "users", Schema = "public" },
            X = 50,
            Y = 50,
            Width = 240,
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "email", DataType = "TEXT", IsNullable = false },
                new() { Name = "name", DataType = "TEXT", IsNullable = true }
            }
        };

        var orders = new ErdTableNode
        {
            Table = new TableInfo { Name = "orders", Schema = "public" },
            X = 400,
            Y = 50,
            Width = 240,
            Columns = new List<ColumnInfo>
            {
                new() { Name = "id", DataType = "INTEGER", IsPrimaryKey = true },
                new() { Name = "user_id", DataType = "INTEGER", IsNullable = false },
                new() { Name = "total", DataType = "REAL", IsNullable = false }
            }
        };

        graph.Nodes.Add(users);
        graph.Nodes.Add(orders);

        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "orders",
            SourceColumn = "user_id",
            TargetTable = "users",
            TargetColumn = "id",
            Cardinality = ErdCardinality.OneToMany
        });

        var authArea = new SubjectAreaModel
        {
            Id = "area_core",
            Title = "Core System",
            X = 20,
            Y = 20,
            Width = 680,
            Height = 300,
            ColorHex = "#008bb9"
        };
        authArea.AddTable("users");
        authArea.AddTable("orders");
        graph.SubjectAreas.Add(authArea);

        return graph;
    }

    [Fact]
    public void SvgErdExporter_ValidGraph_GeneratesXmlCompliantSvg()
    {
        var graph = CreateSampleGraph();
        var svg = SvgErdExporter.ExportSvg(graph);

        Assert.NotNull(svg);
        Assert.StartsWith("<?xml", svg.TrimStart());
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);

        // Validar que o XML gerado é sintaticamente válido
        var doc = XDocument.Parse(svg);
        Assert.Equal("svg", doc.Root?.Name.LocalName);
    }

    [Fact]
    public void SvgErdExporter_CalculatesBoundingBoxCorrectly()
    {
        var graph = CreateSampleGraph();
        var opt = new ErdExportOptions { Padding = 40 };
        var svg = SvgErdExporter.ExportSvg(graph, opt);

        // Bounding box envolve Subject Area (X=20, Y=20, Width=680, Height=300)
        // MinX com padding 40 = 20 - 40 = -20
        var box = DiagramBoundingBox.Calculate(graph, padding: 40);
        Assert.Equal(-20, box.MinX);
        Assert.Equal(-20, box.MinY);
        Assert.Contains("viewBox=\"-20.0 -20.0", svg);
    }

    [Fact]
    public void SvgErdExporter_ThemeSwitching_ChangesBackgroundAndTextColors()
    {
        var graph = CreateSampleGraph();

        var darkSvg = SvgErdExporter.ExportSvg(graph, new ErdExportOptions { Theme = ErdExportTheme.DarkRunix });
        var lightSvg = SvgErdExporter.ExportSvg(graph, new ErdExportOptions { Theme = ErdExportTheme.LightPrint });

        // Dark theme tem fundo escuro #090d14
        Assert.Contains("#090d14", darkSvg);
        // Light theme tem fundo branco puro #ffffff
        Assert.Contains("#ffffff", lightSvg);
        // Ambos contêm os nós de tabela
        Assert.Contains("users", darkSvg);
        Assert.Contains("users", lightSvg);
    }

    [Fact]
    public void SvgErdExporter_ContainsSubjectAreasAndBezierPaths()
    {
        var graph = CreateSampleGraph();
        var svg = SvgErdExporter.ExportSvg(graph);

        Assert.Contains("id=\"subject-areas\"", svg);
        Assert.Contains("Core System", svg);
        Assert.Contains("id=\"relationships\"", svg);
        Assert.Contains("<path d=\"M", svg);
        Assert.Contains("C ", svg);
        Assert.Contains("PK", svg);
    }

    [Fact]
    public void SkiaPdfErdExporter_ValidGraph_GeneratesPdfBinaryHeader()
    {
        var graph = CreateSampleGraph();
        var pdfBytes = SkiaPdfErdExporter.ExportPdf(graph);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 200, "PDF deve conter conteúdo binário substancial");

        var header = Encoding.ASCII.GetString(pdfBytes.AsSpan(0, 5));
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void SkiaPdfErdExporter_PageSizes_ProduceValidPdfs()
    {
        var graph = CreateSampleGraph();

        var fitPdf = SkiaPdfErdExporter.ExportPdf(graph, new ErdExportOptions { PageSize = ErdPdfPageSize.FitToDiagram });
        var a4Pdf = SkiaPdfErdExporter.ExportPdf(graph, new ErdExportOptions { PageSize = ErdPdfPageSize.A4Landscape });
        var a3Pdf = SkiaPdfErdExporter.ExportPdf(graph, new ErdExportOptions { PageSize = ErdPdfPageSize.A3Landscape });

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(fitPdf.AsSpan(0, 5)));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(a4Pdf.AsSpan(0, 5)));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(a3Pdf.AsSpan(0, 5)));
    }

    [Fact]
    public void SkiaPdfErdExporter_DarkAndLightThemes_ProduceValidPdfs()
    {
        var graph = CreateSampleGraph();

        var darkPdf = SkiaPdfErdExporter.ExportPdf(graph, new ErdExportOptions { Theme = ErdExportTheme.DarkRunix });
        var lightPdf = SkiaPdfErdExporter.ExportPdf(graph, new ErdExportOptions { Theme = ErdExportTheme.LightPrint });

        Assert.True(darkPdf.Length > 0);
        Assert.True(lightPdf.Length > 0);
    }

    [Fact]
    public async Task ErdExportService_ExportToFileAsync_WritesSvgAndPdfFiles()
    {
        var service = new ErdExportService();
        var graph = CreateSampleGraph();

        var tempSvg = Path.Combine(Path.GetTempPath(), $"test_erd_{Guid.NewGuid():N}.svg");
        var tempPdf = Path.Combine(Path.GetTempPath(), $"test_erd_{Guid.NewGuid():N}.pdf");

        try
        {
            await service.ExportToFileAsync(tempSvg, graph, new ErdExportOptions { Format = ErdExportFormat.Svg });
            await service.ExportToFileAsync(tempPdf, graph, new ErdExportOptions { Format = ErdExportFormat.Pdf });

            Assert.True(File.Exists(tempSvg));
            Assert.True(new FileInfo(tempSvg).Length > 100);

            Assert.True(File.Exists(tempPdf));
            Assert.True(new FileInfo(tempPdf).Length > 100);
        }
        finally
        {
            if (File.Exists(tempSvg)) File.Delete(tempSvg);
            if (File.Exists(tempPdf)) File.Delete(tempPdf);
        }
    }
}
