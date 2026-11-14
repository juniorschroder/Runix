using System;

namespace Runix.Core.Models;

/// <summary>
/// Representa a definição de uma restrição de Foreign Key para modelagem e migração de schema.
/// </summary>
public record ForeignKeyDefinition
{
    public string SourceTable { get; init; } = string.Empty;
    public string SourceColumn { get; init; } = string.Empty;
    public string TargetTable { get; init; } = string.Empty;
    public string TargetColumn { get; init; } = string.Empty;
    public string ConstraintName { get; init; } = string.Empty;
    public string OnUpdate { get; init; } = "NO ACTION";
    public string OnDelete { get; init; } = "NO ACTION";
    public ErdCardinality Cardinality { get; init; } = ErdCardinality.OneToMany;

    public static string GenerateDefaultConstraintName(string sourceTable, string sourceCol)
    {
        var cleanSource = sourceTable.Replace("\"", "").Trim();
        var cleanCol = sourceCol.Replace("\"", "").Trim();
        return $"fk_{cleanSource}_{cleanCol}";
    }
}
