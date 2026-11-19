using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Models;

namespace Runix.Core.Services;

/// <summary>
/// Motor de auto-layout hierárquico Sugiyama para grafos relacionais (DER).
/// Executa quebra determinística de ciclos via 3-color DFS Feedback Arc Set (FAS),
/// atribuição de camadas topológicas, redução baricêntrica de cruzamentos de arestas
/// e empacotamento em duas fases respeitando Subject Areas (Domínios Visuais).
/// </summary>
public class SugiyamaHierarchicalLayoutEngine : IGraphLayoutEngine
{
    public void ArrangeLayout(ErdGraphModel graph, GraphLayoutOptions? options = null)
    {
        if (graph == null || graph.Nodes.Count == 0) return;

        var opt = options ?? new GraphLayoutOptions();

        // Caso trivial: nó único
        if (graph.Nodes.Count == 1)
        {
            var singleNode = graph.Nodes[0];
            singleNode.X = opt.MarginX;
            singleNode.Y = opt.MarginY;

            if (opt.RespectSubjectAreas && graph.SubjectAreas?.Count > 0)
            {
                var area = graph.SubjectAreas.FirstOrDefault(a => a.TableNames.Contains(singleNode.TableName) || a.ContainsTableCenter(singleNode));
                if (area != null)
                {
                    area.X = Math.Max(0, singleNode.X - opt.SubjectAreaPadding);
                    area.Y = Math.Max(0, singleNode.Y - opt.SubjectAreaPadding - 24);
                    area.Width = singleNode.Width + (opt.SubjectAreaPadding * 2);
                    area.Height = singleNode.Height + (opt.SubjectAreaPadding * 2) + 24;
                    area.AddTable(singleNode.TableName);
                }
            }
            return;
        }

        // Layout com suporte a Subject Areas (Duas fases)
        if (opt.RespectSubjectAreas && graph.SubjectAreas != null && graph.SubjectAreas.Count > 0)
        {
            ArrangeClusteredLayout(graph, opt);
        }
        else
        {
            // Layout global unificado
            ArrangeSubGraph(graph.Nodes, graph.Relationships, opt, opt.MarginX, opt.MarginY);
        }
    }

    /// <summary>
    /// Organiza o diagrama em duas fases: cada Subject Area como cluster independente
    /// e em seguida empacota os domínios e tabelas soltas no canvas global.
    /// </summary>
    private void ArrangeClusteredLayout(ErdGraphModel graph, GraphLayoutOptions opt)
    {
        var assignedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Agrupar nós por Subject Area
        var areaClusters = new List<(SubjectAreaModel Area, List<ErdTableNode> Nodes)>();
        foreach (var area in graph.SubjectAreas)
        {
            var clusterNodes = new List<ErdTableNode>();
            foreach (var node in graph.Nodes)
            {
                if (assignedNodes.Contains(node.NodeKey)) continue;

                bool belongsToArea = area.TableNames.Count > 0
                    ? area.TableNames.Contains(node.NodeKey) || area.TableNames.Contains(node.TableName)
                    : area.ContainsTableCenter(node);

                if (belongsToArea)
                {
                    clusterNodes.Add(node);
                    assignedNodes.Add(node.NodeKey);
                }
            }

            if (clusterNodes.Count > 0)
            {
                areaClusters.Add((area, clusterNodes));
            }
        }

        // Tabelas não vinculadas a nenhuma Subject Area
        var unassignedNodes = graph.Nodes.Where(n => !assignedNodes.Contains(n.NodeKey)).ToList();

        double currentX = opt.MarginX;
        double currentY = opt.MarginY;
        double rowMaxHeight = 0;
        double maxRowWidth = 2400; // Limite de largura de linha para wrap

        // 1. Organizar e posicionar clusters de Subject Areas
        foreach (var (area, clusterNodes) in areaClusters)
        {
            // Sub-relacionamentos internos deste domínio
            var clusterKeys = new HashSet<string>(clusterNodes.Select(n => n.NodeKey), StringComparer.OrdinalIgnoreCase);
            var clusterNames = new HashSet<string>(clusterNodes.Select(n => n.TableName), StringComparer.OrdinalIgnoreCase);
            var internalRels = graph.Relationships
                .Where(r => (clusterKeys.Contains(r.SourceTable) || clusterNames.Contains(r.SourceTable)) &&
                            (clusterKeys.Contains(r.TargetTable) || clusterNames.Contains(r.TargetTable)))
                .ToList();

            // Layout local do subgrafo começando em (0, 0)
            var (clusterW, clusterH) = ArrangeSubGraph(clusterNodes, internalRels, opt, 0, 0);

            // Bounding box da área com padding e cabeçalho (28px no topo para barra de título)
            double headerHeight = 28;
            double areaW = clusterW + (opt.SubjectAreaPadding * 2);
            double areaH = clusterH + (opt.SubjectAreaPadding * 2) + headerHeight;

            // Checar necessidade de quebra de linha (wrap) no canvas
            if (currentX > opt.MarginX && (currentX + areaW) > maxRowWidth)
            {
                currentX = opt.MarginX;
                currentY += rowMaxHeight + opt.DomainSpacing;
                rowMaxHeight = 0;
            }

            area.X = currentX;
            area.Y = currentY;
            area.Width = Math.Max(260, areaW);
            area.Height = Math.Max(180, areaH);

            // Transladar nós para dentro da área (considerando padding e título)
            double innerOffsetX = area.X + opt.SubjectAreaPadding;
            double innerOffsetY = area.Y + opt.SubjectAreaPadding + headerHeight;

            foreach (var node in clusterNodes)
            {
                node.X += innerOffsetX;
                node.Y += innerOffsetY;
                area.AddTable(node.NodeKey);
            }

            currentX += area.Width + opt.DomainSpacing;
            rowMaxHeight = Math.Max(rowMaxHeight, area.Height);
        }

        // 2. Organizar e posicionar tabelas avulsas (unassigned)
        if (unassignedNodes.Count > 0)
        {
            var unassignedKeys = new HashSet<string>(unassignedNodes.Select(n => n.NodeKey), StringComparer.OrdinalIgnoreCase);
            var unassignedNames = new HashSet<string>(unassignedNodes.Select(n => n.TableName), StringComparer.OrdinalIgnoreCase);
            var unassignedRels = graph.Relationships
                .Where(r => (unassignedKeys.Contains(r.SourceTable) || unassignedNames.Contains(r.SourceTable)) &&
                            (unassignedKeys.Contains(r.TargetTable) || unassignedNames.Contains(r.TargetTable)))
                .ToList();

            // Se já posicionamos áreas, colocar as tabelas avulsas em uma nova linha ou ao lado
            if (areaClusters.Count > 0)
            {
                currentX = opt.MarginX;
                currentY += rowMaxHeight + opt.DomainSpacing;
            }

            ArrangeSubGraph(unassignedNodes, unassignedRels, opt, currentX, currentY);
        }
    }

    /// <summary>
    /// Executa o pipeline Sugiyama em um subconjunto de nós e relacionamentos.
    /// Retorna a largura e altura total delimitada pelo subgrafo.
    /// </summary>
    private (double Width, double Height) ArrangeSubGraph(
        List<ErdTableNode> nodes,
        List<ErdRelationshipLink> relationships,
        GraphLayoutOptions opt,
        double startX,
        double startY)
    {
        if (nodes.Count == 0) return (0, 0);

        if (nodes.Count == 1)
        {
            var single = nodes[0];
            single.X = startX;
            single.Y = startY;
            return (single.Width, single.Height);
        }

        var nodeKeys = nodes.Select(n => n.NodeKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var nodeByKey = new Dictionary<string, ErdTableNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            nodeByKey[n.NodeKey] = n;
            nodeByKey.TryAdd(n.TableName, n);
        }

        // FASE 1: Construir grafo de adjacências e quebrar ciclos com 3-Color DFS FAS
        // Na semântica relacional, se SourceTable tem FK apontando para TargetTable,
        // TargetTable é o Pai (independente) e SourceTable é o Filho (dependente).
        // Fluxo top-down: TargetTable -> SourceTable
        var forwardAdj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys)
        {
            forwardAdj[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rel in relationships)
        {
            // Ignorar auto-referência (ex: employees.manager_id -> employees.id) para ranking
            if (string.Equals(rel.SourceTable, rel.TargetTable, StringComparison.OrdinalIgnoreCase))
                continue;

            if (nodeByKey.TryGetValue(rel.SourceTable, out var srcNode) &&
                nodeByKey.TryGetValue(rel.TargetTable, out var tgtNode))
            {
                var srcKey = srcNode.NodeKey;
                var tgtKey = tgtNode.NodeKey;
                if (!string.Equals(srcKey, tgtKey, StringComparison.OrdinalIgnoreCase) &&
                    forwardAdj.TryGetValue(tgtKey, out var adj))
                {
                    // Aresta pai -> filho
                    adj.Add(srcKey);
                }
            }
        }

        // 3-Color DFS: 0 = White (não visitado), 1 = Gray (na pilha de recursão), 2 = Black (finalizado)
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys) color[key] = 0;

        var dagEdges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys) dagEdges[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string u)
        {
            color[u] = 1; // Gray
            if (forwardAdj.TryGetValue(u, out var adjSet))
            {
                foreach (var v in adjSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    if (color.TryGetValue(v, out var vColor) && vColor == 1)
                    {
                        // Back-edge detectada (ciclo)! Reverter ou ignorar da topologia DAG
                        if (dagEdges.TryGetValue(v, out var revEdges))
                        {
                            revEdges.Add(u);
                        }
                    }
                    else
                    {
                        if (dagEdges.TryGetValue(u, out var uEdges))
                        {
                            uEdges.Add(v);
                        }
                        if (vColor == 0)
                        {
                            Dfs(v);
                        }
                    }
                }
            }
            color[u] = 2; // Black
        }

        foreach (var key in nodeKeys)
        {
            if (color.TryGetValue(key, out var c) && c == 0)
            {
                Dfs(key);
            }
        }

        // FASE 2: Layering / Rank Assignment (Atribuição de Camadas via Longest Path em DAG)
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys) inDegree[key] = 0;

        foreach (var kvp in dagEdges)
        {
            foreach (var target in kvp.Value)
            {
                if (inDegree.ContainsKey(target))
                {
                    inDegree[target]++;
                }
            }
        }

        var rankMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys) rankMap[key] = 0;

        var queue = new Queue<string>();
        foreach (var key in nodeKeys.Where(k => inDegree.TryGetValue(k, out var d) && d == 0).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            queue.Enqueue(key);
        }

        int processedCount = 0;
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            processedCount++;
            int currentRank = rankMap.TryGetValue(u, out var rk) ? rk : 0;

            if (dagEdges.TryGetValue(u, out var edges))
            {
                foreach (var v in edges)
                {
                    if (rankMap.ContainsKey(v))
                    {
                        rankMap[v] = Math.Max(rankMap[v], currentRank + 1);
                    }
                    if (inDegree.ContainsKey(v))
                    {
                        inDegree[v]--;
                        if (inDegree[v] == 0)
                        {
                            queue.Enqueue(v);
                        }
                    }
                }
            }
        }

        // Se sobraram nós (devido a arestas reversas secundárias), garantir rank válido
        if (processedCount < nodeKeys.Count)
        {
            foreach (var key in nodeKeys.Where(k => !rankMap.ContainsKey(k)))
            {
                rankMap[key] = 0;
            }
        }

        // Agrupar nós por camada (Rank)
        var layers = new SortedDictionary<int, List<ErdTableNode>>();
        foreach (var node in nodes)
        {
            int r = rankMap.TryGetValue(node.NodeKey, out var val) ? val : 0;
            if (!layers.TryGetValue(r, out var layerList))
            {
                layerList = new List<ErdTableNode>();
                layers[r] = layerList;
            }
            layerList.Add(node);
        }

        // Ordenar nós inicialmente por nome dentro da camada
        foreach (var layer in layers.Values)
        {
            layer.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        }

        // FASE 3: Crossing Reduction (Heurística Baricêntrica de Cruzamento)
        // Mapear relacionamentos bidirecionais locais para cálculo de vizinhos
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in nodeKeys) neighbors[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in relationships)
        {
            if (nodeByKey.TryGetValue(rel.SourceTable, out var srcNode) &&
                nodeByKey.TryGetValue(rel.TargetTable, out var tgtNode))
            {
                var srcKey = srcNode.NodeKey;
                var tgtKey = tgtNode.NodeKey;
                if (!string.Equals(srcKey, tgtKey, StringComparison.OrdinalIgnoreCase) &&
                    neighbors.TryGetValue(srcKey, out var srcNbrs) &&
                    neighbors.TryGetValue(tgtKey, out var tgtNbrs))
                {
                    srcNbrs.Add(tgtKey);
                    tgtNbrs.Add(srcKey);
                }
            }
        }

        int maxRank = layers.Keys.Count > 0 ? layers.Keys.Max() : 0;
        // 2 varreduras (downward e upward)
        for (int iter = 0; iter < 2; iter++)
        {
            // Varredura descendente (Top -> Bottom)
            for (int r = 1; r <= maxRank; r++)
            {
                if (!layers.ContainsKey(r) || !layers.ContainsKey(r - 1)) continue;

                var prevLayer = layers[r - 1];
                var prevPos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < prevLayer.Count; i++) prevPos[prevLayer[i].NodeKey] = i;

                layers[r] = layers[r]
                    .OrderBy(node =>
                    {
                        var connected = neighbors.TryGetValue(node.NodeKey, out var nbrs)
                            ? nbrs.Where(prevPos.ContainsKey).ToList()
                            : new List<string>();
                        if (connected.Count == 0) return 0.0;
                        return connected.Average(c => prevPos[c]);
                    })
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Varredura ascendente (Bottom -> Top)
            for (int r = maxRank - 1; r >= 0; r--)
            {
                if (!layers.ContainsKey(r) || !layers.ContainsKey(r + 1)) continue;

                var nextLayer = layers[r + 1];
                var nextPos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < nextLayer.Count; i++) nextPos[nextLayer[i].NodeKey] = i;

                layers[r] = layers[r]
                    .OrderBy(node =>
                    {
                        var connected = neighbors.TryGetValue(node.NodeKey, out var nbrs)
                            ? nbrs.Where(nextPos.ContainsKey).ToList()
                            : new List<string>();
                        if (connected.Count == 0) return 0.0;
                        return connected.Average(c => nextPos[c]);
                    })
                    .ThenBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        // FASE 4: Coordinate Assignment (Atribuição de Coordenadas Geométricas)
        double totalWidth = 0;
        double totalHeight = 0;

        if (opt.Direction == LayoutDirection.TopToBottom)
        {
            // Cada rank é uma linha horizontal (Y avança por camada; nós se distribuem em X)
            double currentLayerY = startY;

            // Calcular larguras totais por camada para centralização estética
            var layerWidths = new Dictionary<int, double>();
            foreach (var (r, layerNodes) in layers)
            {
                double w = layerNodes.Sum(n => n.Width) + ((layerNodes.Count - 1) * opt.NodeSpacingX);
                layerWidths[r] = w;
                totalWidth = Math.Max(totalWidth, w);
            }

            foreach (var (r, layerNodes) in layers)
            {
                double maxNodeHeight = layerNodes.Max(n => n.Height);
                double layerWidth = layerWidths[r];
                double layerStartX = startX + Math.Max(0, (totalWidth - layerWidth) / 2.0);

                double currentX = layerStartX;
                foreach (var node in layerNodes)
                {
                    node.X = currentX;
                    node.Y = currentLayerY;
                    currentX += node.Width + opt.NodeSpacingX;
                }

                currentLayerY += maxNodeHeight + opt.NodeSpacingY;
            }

            totalHeight = currentLayerY - startY - opt.NodeSpacingY;
        }
        else // LayoutDirection.LeftToRight
        {
            // Cada rank é uma coluna vertical (X avança por camada; nós se distribuem em Y)
            double currentLayerX = startX;

            var layerHeights = new Dictionary<int, double>();
            foreach (var (r, layerNodes) in layers)
            {
                double h = layerNodes.Sum(n => n.Height) + ((layerNodes.Count - 1) * opt.NodeSpacingY);
                layerHeights[r] = h;
                totalHeight = Math.Max(totalHeight, h);
            }

            foreach (var (r, layerNodes) in layers)
            {
                double maxNodeWidth = layerNodes.Max(n => n.Width);
                double layerHeight = layerHeights[r];
                double layerStartY = startY + Math.Max(0, (totalHeight - layerHeight) / 2.0);

                double currentY = layerStartY;
                foreach (var node in layerNodes)
                {
                    node.X = currentLayerX;
                    node.Y = currentY;
                    currentY += node.Height + opt.NodeSpacingY;
                }

                currentLayerX += maxNodeWidth + opt.NodeSpacingX;
            }

            totalWidth = currentLayerX - startX - opt.NodeSpacingX;
        }

        return (totalWidth, totalHeight);
    }
}
