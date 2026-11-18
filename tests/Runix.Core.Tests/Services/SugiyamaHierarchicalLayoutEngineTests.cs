using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Runix.Core.Models;
using Runix.Core.Services;
using Xunit;

namespace Runix.Core.Tests.Services;

public class SugiyamaHierarchicalLayoutEngineTests
{
    private readonly SugiyamaHierarchicalLayoutEngine _engine = new();

    private static ErdTableNode CreateNode(string tableName, int columnCount = 4)
    {
        var node = new ErdTableNode
        {
            Table = new TableInfo { Name = tableName, Schema = "public" },
            Width = 240
        };

        for (int i = 0; i < columnCount; i++)
        {
            node.Columns.Add(new ColumnInfo
            {
                Name = i == 0 ? "id" : $"col_{i}",
                DataType = "INTEGER",
                IsPrimaryKey = i == 0
            });
        }

        return node;
    }

    [Fact]
    public void ArrangeLayout_NullOrEmptyGraph_DoesNotThrow()
    {
        _engine.ArrangeLayout(null!);
        var empty = new ErdGraphModel();
        _engine.ArrangeLayout(empty);

        Assert.Empty(empty.Nodes);
    }

    [Fact]
    public void ArrangeLayout_SingleNode_PositionsAtMargins()
    {
        var graph = new ErdGraphModel();
        var node = CreateNode("users");
        graph.Nodes.Add(node);

        var opt = new GraphLayoutOptions { MarginX = 50, MarginY = 60 };
        _engine.ArrangeLayout(graph, opt);

        Assert.Equal(50, node.X);
        Assert.Equal(60, node.Y);
    }

    [Fact]
    public void ArrangeLayout_LinearHierarchy_PositionsParentAboveChild()
    {
        var graph = new ErdGraphModel();
        var users = CreateNode("users");
        var orders = CreateNode("orders");
        var items = CreateNode("order_items");

        graph.Nodes.AddRange(new[] { users, orders, items });

        // orders.user_id -> users.id (orders is child, users is parent)
        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "orders",
            SourceColumn = "user_id",
            TargetTable = "users",
            TargetColumn = "id"
        });

        // items.order_id -> orders.id (items is child, orders is parent)
        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "order_items",
            SourceColumn = "order_id",
            TargetTable = "orders",
            TargetColumn = "id"
        });

        _engine.ArrangeLayout(graph, new GraphLayoutOptions { Direction = LayoutDirection.TopToBottom });

        // Em TopToBottom: users (parent) no topo (menor Y), orders no meio, items abaixo (maior Y)
        Assert.True(users.Y < orders.Y, $"Esperado users.Y ({users.Y}) < orders.Y ({orders.Y})");
        Assert.True(orders.Y < items.Y, $"Esperado orders.Y ({orders.Y}) < items.Y ({items.Y})");
    }

    [Fact]
    public void ArrangeLayout_CyclicGraph_BreaksCycleDeterministically()
    {
        var graph = new ErdGraphModel();
        var a = CreateNode("table_a");
        var b = CreateNode("table_b");
        var c = CreateNode("table_c");

        graph.Nodes.AddRange(new[] { a, b, c });

        // Ciclo: A -> B -> C -> A
        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "table_b", TargetTable = "table_a" });
        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "table_c", TargetTable = "table_b" });
        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "table_a", TargetTable = "table_c" });

        // Deve convergir sem loop infinito e definir coordenadas válidas
        _engine.ArrangeLayout(graph);

        foreach (var node in graph.Nodes)
        {
            Assert.True(node.X >= 0, $"Nó {node.TableName} tem X inválido: {node.X}");
            Assert.True(node.Y >= 0, $"Nó {node.TableName} tem Y inválido: {node.Y}");
        }
    }

    [Fact]
    public void ArrangeLayout_SelfReferencingNode_DoesNotLoop()
    {
        var graph = new ErdGraphModel();
        var employees = CreateNode("employees");
        graph.Nodes.Add(employees);

        // Auto-referência: employees.manager_id -> employees.id
        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "employees",
            SourceColumn = "manager_id",
            TargetTable = "employees",
            TargetColumn = "id"
        });

        _engine.ArrangeLayout(graph);

        Assert.Equal(40, employees.X);
        Assert.Equal(40, employees.Y);
    }

    [Fact]
    public void ArrangeLayout_LeftToRightDirection_PositionsCorrectly()
    {
        var graph = new ErdGraphModel();
        var users = CreateNode("users");
        var orders = CreateNode("orders");

        graph.Nodes.AddRange(new[] { users, orders });
        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "orders",
            TargetTable = "users"
        });

        _engine.ArrangeLayout(graph, new GraphLayoutOptions { Direction = LayoutDirection.LeftToRight });

        // Em LeftToRight: users (parent) à esquerda (menor X), orders à direita (maior X)
        Assert.True(users.X < orders.X, $"Esperado users.X ({users.X}) < orders.X ({orders.X})");
    }

    [Fact]
    public void ArrangeLayout_SubjectAreaClustering_EnclosesAndPacksDomains()
    {
        var graph = new ErdGraphModel();
        var u1 = CreateNode("auth_users");
        var u2 = CreateNode("auth_roles");
        var b1 = CreateNode("billing_invoices");
        var b2 = CreateNode("billing_payments");
        var free = CreateNode("system_logs");

        graph.Nodes.AddRange(new[] { u1, u2, b1, b2, free });

        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "auth_users", TargetTable = "auth_roles" });
        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "billing_payments", TargetTable = "billing_invoices" });

        var authArea = new SubjectAreaModel { Id = "area_auth", Title = "Autenticação" };
        authArea.AddTable("auth_users");
        authArea.AddTable("auth_roles");

        var billingArea = new SubjectAreaModel { Id = "area_billing", Title = "Faturamento" };
        billingArea.AddTable("billing_invoices");
        billingArea.AddTable("billing_payments");

        graph.SubjectAreas.Add(authArea);
        graph.SubjectAreas.Add(billingArea);

        _engine.ArrangeLayout(graph, new GraphLayoutOptions { RespectSubjectAreas = true });

        // Verificar que as áreas envolvem suas respectivas tabelas
        Assert.True(authArea.ContainsTableCenter(u1), "authArea deve conter auth_users");
        Assert.True(authArea.ContainsTableCenter(u2), "authArea deve conter auth_roles");
        Assert.True(billingArea.ContainsTableCenter(b1), "billingArea deve conter billing_invoices");
        Assert.True(billingArea.ContainsTableCenter(b2), "billingArea deve conter billing_payments");

        // As duas áreas não devem se sobrepor exatamente nas mesmas coordenadas
        Assert.False(authArea.X == billingArea.X && authArea.Y == billingArea.Y, "Áreas devem ocupar posições distintas");
    }

    [Fact]
    public void ArrangeLayout_PerformanceUnder10msForLargeSchema()
    {
        var graph = new ErdGraphModel();
        const int tableCount = 60;

        for (int i = 0; i < tableCount; i++)
        {
            graph.Nodes.Add(CreateNode($"tbl_{i:D2}", columnCount: 5));
        }

        // Criar múltiplos relacionamentos e alguns ciclos
        for (int i = 1; i < tableCount; i++)
        {
            graph.Relationships.Add(new ErdRelationshipLink
            {
                SourceTable = $"tbl_{i:D2}",
                TargetTable = $"tbl_{(i / 2):D2}"
            });
        }

        // Ciclo proposital
        graph.Relationships.Add(new ErdRelationshipLink
        {
            SourceTable = "tbl_00",
            TargetTable = "tbl_10"
        });

        // Warm-up
        _engine.ArrangeLayout(graph);

        var sw = Stopwatch.StartNew();
        _engine.ArrangeLayout(graph);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"Layout demorou {sw.ElapsedMilliseconds}ms, esperado < 50ms (idealmente < 10ms)");
    }

    [Fact]
    public void ErdLayoutService_ApplyTopologicalLayout_DelegatesToSugiyama()
    {
        var graph = new ErdGraphModel();
        var parent = CreateNode("parent");
        var child = CreateNode("child");
        graph.Nodes.AddRange(new[] { parent, child });
        graph.Relationships.Add(new ErdRelationshipLink { SourceTable = "child", TargetTable = "parent" });

        ErdLayoutService.ApplyTopologicalLayout(graph);

        Assert.True(parent.Y < child.Y, "ErdLayoutService deve aplicar ordenação hierárquica Sugiyama");
    }
}
