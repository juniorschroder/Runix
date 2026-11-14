namespace Runix.Core.Models;

/// <summary>
/// Representa metadados de uma chave estrangeira e restrições de integridade referencial.
/// </summary>
public record ForeignKeyInfo
{
    public string ConstraintName { get; init; } = "";
    public string SourceTable { get; init; } = "";
    public string SourceColumn { get; init; } = "";
    public string TargetTable { get; init; } = "";
    public string TargetColumn { get; init; } = "";
    public string OnUpdate { get; init; } = "NO ACTION";
    public string OnDelete { get; init; } = "NO ACTION";
}
