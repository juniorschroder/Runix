using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Representa o nó visual de uma tabela no canvas do DER com geometria e metadados.
/// </summary>
public class ErdTableNode
{
    public TableInfo Table { get; set; } = new();
    public List<ColumnInfo> Columns { get; set; } = new();

    public string TableName => Table.Name;
    public string Schema => Table.Schema;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 250;

    /// <summary>
    /// Altura calculada dinamicamente com base no cabeçalho + número de colunas.
    /// </summary>
    public double Height => Math.Max(100, 48 + (Columns.Count * 26));

    public bool IsSelected { get; set; }
}
