using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runix.Core.Models;

namespace Runix.Core.Services;

/// <summary>
/// Serviço de inteligência relacional para cálculo de layout topológico e inferência de cardinalidades de DER.
/// </summary>
public static class ErdLayoutService
{
    private static readonly string LayoutsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runix", "layouts");

    /// <summary>
    /// Infere a cardinalidade de uma chave estrangeira analisando unicidade e tabelas de junção.
    /// </summary>
    public static ErdCardinality InferCardinality(
        ForeignKeyInfo fk,
        List<ColumnInfo> sourceColumns,
        List<ForeignKeyInfo> allSourceFks,
        List<string>? uniqueColumnNames = null)
    {
        var pkCols = sourceColumns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        // Se a tabela tem exatamente 2 FKs e ambas compõem as chaves primárias, é uma tabela de junção N:M
        if (allSourceFks.Count == 2 && pkCols.Count == 2 &&
            allSourceFks.All(f => pkCols.Any(pk => string.Equals(pk, f.SourceColumn, StringComparison.OrdinalIgnoreCase))))
        {
            return ErdCardinality.ManyToMany;
        }

        var col = sourceColumns.FirstOrDefault(c => string.Equals(c.Name, fk.SourceColumn, StringComparison.OrdinalIgnoreCase));

        // Se a coluna de origem da FK for Chave Primária exclusiva (não composta) ou tiver restrição UNIQUE, a relação é 1:1
        bool isUnique = (col != null && col.IsPrimaryKey && pkCols.Count == 1) ||
                        (uniqueColumnNames?.Any(u => string.Equals(u, fk.SourceColumn, StringComparison.OrdinalIgnoreCase)) ?? false);

        if (isUnique)
        {
            return ErdCardinality.OneToOne;
        }

        return ErdCardinality.OneToMany;
    }

    /// <summary>
    /// Aplica distribuição em colunas hierárquicas ordenadas por dependência topológica.
    /// </summary>
    public static void ApplyTopologicalLayout(ErdGraphModel graph)
    {
        if (graph.Nodes.Count == 0) return;

        // Mapear dependências (tabelas que a tabela A aponta via FK)
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Nodes)
        {
            dependencies[node.TableName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rel in graph.Relationships)
        {
            if (dependencies.TryGetValue(rel.SourceTable, out var set) && !string.Equals(rel.SourceTable, rel.TargetTable, StringComparison.OrdinalIgnoreCase))
            {
                set.Add(rel.TargetTable);
            }
        }

        // Calcular profundidade (coluna) de cada tabela
        var columnLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Algoritmo iterativo de atribuição de camadas
        foreach (var node in graph.Nodes)
        {
            columnLevels[node.TableName] = 0;
        }

        bool changed = true;
        int iterations = 0;
        while (changed && iterations < graph.Nodes.Count * 2)
        {
            changed = false;
            iterations++;

            foreach (var node in graph.Nodes)
            {
                var deps = dependencies[node.TableName];
                if (deps.Count > 0)
                {
                    int maxTargetCol = -1;
                    foreach (var target in deps)
                    {
                        if (columnLevels.TryGetValue(target, out var targetLevel))
                        {
                            maxTargetCol = Math.Max(maxTargetCol, targetLevel);
                        }
                    }

                    int newCol = maxTargetCol + 1;
                    if (newCol != columnLevels[node.TableName])
                    {
                        columnLevels[node.TableName] = newCol;
                        changed = true;
                    }
                }
            }
        }

        // Agrupar nós por coluna
        var columns = new Dictionary<int, List<ErdTableNode>>();
        foreach (var node in graph.Nodes)
        {
            var col = columnLevels[node.TableName];
            if (!columns.TryGetValue(col, out var list))
            {
                list = new List<ErdTableNode>();
                columns[col] = list;
            }
            list.Add(node);
        }

        // Atribuir coordenadas espaciais
        const double ColWidth = 360;
        const double StartX = 40;
        const double StartY = 40;
        const double SpacingY = 36;

        foreach (var (colIdx, nodesInCol) in columns.OrderBy(kv => kv.Key))
        {
            double currentY = StartY;
            double currentX = StartX + (colIdx * ColWidth);

            foreach (var node in nodesInCol)
            {
                node.X = currentX;
                node.Y = currentY;
                currentY += node.Height + SpacingY;
            }
        }
    }

    /// <summary>
    /// Salva posições coordenadas do grafo em arquivo JSON local.
    /// </summary>
    public static void SaveLayout(string databaseId, ErdGraphModel graph)
    {
        try
        {
            Directory.CreateDirectory(LayoutsDir);
            var safeName = SanitizeFileName(databaseId) + ".json";
            var filePath = Path.Combine(LayoutsDir, safeName);

            var dict = graph.Nodes.ToDictionary(n => n.TableName, n => new NodeCoordinates { X = n.X, Y = n.Y });
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Silencioso em caso de permissão de disco
        }
    }

    /// <summary>
    /// Restaura posições coordenadas salvas se existirem.
    /// </summary>
    public static bool LoadLayout(string databaseId, ErdGraphModel graph)
    {
        try
        {
            var safeName = SanitizeFileName(databaseId) + ".json";
            var filePath = Path.Combine(LayoutsDir, safeName);
            if (!File.Exists(filePath)) return false;

            var json = File.ReadAllText(filePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, NodeCoordinates>>(json);
            if (dict == null) return false;

            foreach (var node in graph.Nodes)
            {
                if (dict.TryGetValue(node.TableName, out var coord))
                {
                    node.X = coord.X;
                    node.Y = coord.Y;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private record NodeCoordinates
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
