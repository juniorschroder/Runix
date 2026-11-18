using System;
using System.Collections.Generic;
using System.IO;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class ErdLayoutServicePersistenceTests
{
    private readonly string _testDbId = $"test_db_{Guid.NewGuid():N}";
    private readonly string _layoutsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runix", "layouts");

    private void CleanupTestFile()
    {
        try
        {
            var filePath = Path.Combine(_layoutsDir, $"{_testDbId}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void SaveAndLoad_V2Envelope_PreservesNodesAndSubjectAreas()
    {
        try
        {
            var graph = new ErdGraphModel();
            graph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "users" }, X = 150, Y = 200 });
            graph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "orders" }, X = 500, Y = 200 });

            var area = new SubjectAreaModel
            {
                Title = "Vendas e Faturamento",
                Description = "Módulo principal",
                ColorHex = SubjectAreaPalette.Emerald,
                X = 100,
                Y = 120,
                Width = 600,
                Height = 350
            };
            area.AddTable("users");
            area.AddTable("orders");
            graph.SubjectAreas.Add(area);

            // 1. Save
            ErdLayoutService.SaveLayout(_testDbId, graph);

            // 2. Load into new graph
            var loadedGraph = new ErdGraphModel();
            loadedGraph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "users" } });
            loadedGraph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "orders" } });

            bool loaded = ErdLayoutService.LoadLayout(_testDbId, loadedGraph);

            Assert.True(loaded);
            var usersNode = loadedGraph.FindNode("users");
            var ordersNode = loadedGraph.FindNode("orders");

            Assert.NotNull(usersNode);
            Assert.Equal(150, usersNode.X);
            Assert.Equal(200, usersNode.Y);

            Assert.NotNull(ordersNode);
            Assert.Equal(500, ordersNode.X);
            Assert.Equal(200, ordersNode.Y);

            Assert.Single(loadedGraph.SubjectAreas);
            var loadedArea = loadedGraph.SubjectAreas[0];
            Assert.Equal("Vendas e Faturamento", loadedArea.Title);
            Assert.Equal(SubjectAreaPalette.Emerald, loadedArea.ColorHex);
            Assert.Equal(2, loadedArea.TableCount);
        }
        finally
        {
            CleanupTestFile();
        }
    }

    [Fact]
    public void LoadLayout_LegacyV1FlatDictionary_SuccessfullyRestoresPositions()
    {
        try
        {
            Directory.CreateDirectory(_layoutsDir);
            var filePath = Path.Combine(_layoutsDir, $"{_testDbId}.json");

            // Write legacy v1 JSON directly (flat dictionary without "version" and without "subjectAreas")
            var legacyJson = "{\n  \"customers\": {\n    \"X\": 80,\n    \"Y\": 140\n  },\n  \"logs\": {\n    \"X\": 320,\n    \"Y\": 400\n  }\n}";
            File.WriteAllText(filePath, legacyJson);

            var graph = new ErdGraphModel();
            graph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "customers" } });
            graph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "logs" } });

            bool loaded = ErdLayoutService.LoadLayout(_testDbId, graph);

            Assert.True(loaded);
            Assert.Equal(80, graph.FindNode("customers")?.X);
            Assert.Equal(140, graph.FindNode("customers")?.Y);
            Assert.Equal(320, graph.FindNode("logs")?.X);
            Assert.Equal(400, graph.FindNode("logs")?.Y);
            Assert.Empty(graph.SubjectAreas);
        }
        finally
        {
            CleanupTestFile();
        }
    }

    [Fact]
    public void SafeUngroup_RemovingSubjectAreaDoesNotAffectTableCoordinates()
    {
        try
        {
            var graph = new ErdGraphModel();
            graph.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "products" }, X = 220, Y = 330 });

            var area = new SubjectAreaModel { Title = "Catálogo", X = 200, Y = 300, Width = 300, Height = 200 };
            area.AddTable("products");
            graph.SubjectAreas.Add(area);

            // Save with area
            ErdLayoutService.SaveLayout(_testDbId, graph);

            // Safe ungroup: remove area and save again
            graph.SubjectAreas.Clear();
            ErdLayoutService.SaveLayout(_testDbId, graph);

            // Reload
            var reloaded = new ErdGraphModel();
            reloaded.Nodes.Add(new ErdTableNode { Table = new TableInfo { Name = "products" } });
            bool loaded = ErdLayoutService.LoadLayout(_testDbId, reloaded);

            Assert.True(loaded);
            Assert.Equal(220, reloaded.FindNode("products")?.X);
            Assert.Equal(330, reloaded.FindNode("products")?.Y);
            Assert.Empty(reloaded.SubjectAreas);
        }
        finally
        {
            CleanupTestFile();
        }
    }
}
