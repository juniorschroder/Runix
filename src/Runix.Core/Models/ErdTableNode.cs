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

    /// <summary>
    /// Identificador único do nó no diagrama (ex: 'public.users' ou 'users').
    /// </summary>
    public string NodeKey => string.IsNullOrEmpty(Schema) || Schema.Equals("public", StringComparison.OrdinalIgnoreCase) || Schema.Equals("main", StringComparison.OrdinalIgnoreCase)
        ? TableName
        : $"{Schema}.{TableName}";

    /// <summary>
    /// Nome de exibição respeitando o schema quando aplicável (ex: 'audit.users' ou 'users').
    /// </summary>
    public string DisplayName => Table?.DisplayName ?? TableName;

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 250;

    /// <summary>
    /// Altura calculada dinamicamente com base no cabeçalho + número de colunas.
    /// </summary>
    public double Height => Math.Max(100, 48 + (Columns.Count * 26));

    public bool IsSelected { get; set; }
}
