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

    private static readonly IGraphLayoutEngine DefaultLayoutEngine = new SugiyamaHierarchicalLayoutEngine();

    /// <summary>
    /// Aplica distribuição em colunas hierárquicas ordenadas por dependência topológica via algoritmo Sugiyama.
    /// </summary>
    public static void ApplyTopologicalLayout(ErdGraphModel graph, GraphLayoutOptions? options = null)
    {
        if (graph == null || graph.Nodes.Count == 0) return;
        DefaultLayoutEngine.ArrangeLayout(graph, options);
    }

    /// <summary>
    /// Salva posições coordenadas do grafo e Subject Areas em envelope JSON local (v2).
    /// </summary>
    public static void SaveLayout(string databaseId, ErdGraphModel graph)
    {
        try
        {
            Directory.CreateDirectory(LayoutsDir);
            var safeName = SanitizeFileName(databaseId) + ".json";
            var filePath = Path.Combine(LayoutsDir, safeName);

            var nodesDict = new Dictionary<string, NodeCoordinates>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in graph.Nodes)
            {
                nodesDict[n.NodeKey] = new NodeCoordinates { X = n.X, Y = n.Y };
                nodesDict.TryAdd(n.TableName, new NodeCoordinates { X = n.X, Y = n.Y });
            }

            var envelope = new DiagramLayoutEnvelope
            {
                Version = 2,
                Nodes = nodesDict,
                SubjectAreas = graph.SubjectAreas ?? new List<SubjectAreaModel>()
            };

            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Silencioso em caso de permissão de disco
        }
    }

    /// <summary>
    /// Restaura posições coordenadas e Subject Areas salvas, com suporte retrocompatível para envelopes v2 e layouts legados v1.
    /// </summary>
    public static bool LoadLayout(string databaseId, ErdGraphModel graph)
    {
        try
        {
            var safeName = SanitizeFileName(databaseId) + ".json";
            var filePath = Path.Combine(LayoutsDir, safeName);
            if (!File.Exists(filePath)) return false;

            var json = File.ReadAllText(filePath);

            // 1. Se contiver a propriedade "version", deserializar como envelope v2
            if (json.Contains("\"version\"", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize<DiagramLayoutEnvelope>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (envelope != null && envelope.Version >= 2)
                    {
                        if (envelope.Nodes != null)
                        {
                            foreach (var node in graph.Nodes)
                            {
                                if (envelope.Nodes.TryGetValue(node.NodeKey, out var coord) ||
                                    envelope.Nodes.TryGetValue(node.TableName, out coord))
                                {
                                    node.X = coord.X;
                                    node.Y = coord.Y;
                                }
                            }
                        }

                        graph.SubjectAreas = envelope.SubjectAreas ?? new List<SubjectAreaModel>();
                        return true;
                    }
                }
                catch
                {
                    // Fallback para layout legado v1 se houver corrupção no envelope
                }
            }

            // 2. Fallback retrocompatível: layout legado v1 (Dictionary<string, NodeCoordinates>)
            var legacyDict = JsonSerializer.Deserialize<Dictionary<string, NodeCoordinates>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (legacyDict != null)
            {
                foreach (var node in graph.Nodes)
                {
                    if (legacyDict.TryGetValue(node.NodeKey, out var coord) ||
                        legacyDict.TryGetValue(node.TableName, out coord))
                    {
                        node.X = coord.X;
                        node.Y = coord.Y;
                    }
                }
                graph.SubjectAreas.Clear();
                return true;
            }

            return false;
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

    public class DiagramLayoutEnvelope
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, NodeCoordinates> Nodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SubjectAreaModel> SubjectAreas { get; set; } = new();
    }

    public class NodeCoordinates
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}
