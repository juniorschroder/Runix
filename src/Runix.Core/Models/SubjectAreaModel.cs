using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Modelo conceitual de uma Subject Area (Domínio Visual / Área Temática) no DER.
/// Delimita módulos funcionais no canvas para organização de tabelas e arraste em lote.
/// </summary>
public class SubjectAreaModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Nova Área";
    public string? Description { get; set; }
    public string ColorHex { get; set; } = SubjectAreaPalette.DefaultColor;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420.0;
    public double Height { get; set; } = 320.0;
    public HashSet<string> TableNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int TableCount => TableNames.Count;

    /// <summary>
    /// Avalia se o ponto central de um nó de tabela está contido no retângulo da Subject Area.
    /// </summary>
    public bool ContainsTableCenter(ErdTableNode node)
    {
        if (node == null) return false;
        double nodeW = node.Width > 0 ? node.Width : 220.0;
        double nodeH = node.Height > 0 ? node.Height : 180.0;
        return ContainsTableCenter(node.X, node.Y, nodeW, nodeH);
    }

    /// <summary>
    /// Avalia se o centro da caixa delimitadora informada está contido no retângulo da Subject Area.
    /// </summary>
    public bool ContainsTableCenter(double nodeX, double nodeY, double nodeWidth, double nodeHeight)
    {
        double centerX = nodeX + (nodeWidth / 2.0);
        double centerY = nodeY + (nodeHeight / 2.0);

        return centerX >= X && centerX <= (X + Width) &&
               centerY >= Y && centerY <= (Y + Height);
    }

    public void AddTable(string tableName)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            TableNames.Add(tableName.Trim());
        }
    }

    public void RemoveTable(string tableName)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            TableNames.Remove(tableName.Trim());
        }
    }
}
