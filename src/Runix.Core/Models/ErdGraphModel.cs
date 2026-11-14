using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models;

/// <summary>
/// Modelo conceitual completo do grafo relacional do banco de dados para o DER.
/// </summary>
public class ErdGraphModel
{
    public List<ErdTableNode> Nodes { get; set; } = new();
    public List<ErdRelationshipLink> Relationships { get; set; } = new();

    public ErdTableNode? FindNode(string tableName)
    {
        return Nodes.FirstOrDefault(n => string.Equals(n.TableName, tableName, System.StringComparison.OrdinalIgnoreCase));
    }
}
