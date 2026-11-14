namespace Runix.Core.Models;

/// <summary>
/// Representa uma conexão relacional direcionada entre duas tabelas no diagrama DER.
/// </summary>
public record ErdRelationshipLink
{
    public string Id => $"{SourceTable}.{SourceColumn}->{TargetTable}.{TargetColumn}";
    public string SourceTable { get; init; } = "";
    public string SourceColumn { get; init; } = "";
    public string TargetTable { get; init; } = "";
    public string TargetColumn { get; init; } = "";
    public ErdCardinality Cardinality { get; init; } = ErdCardinality.OneToMany;
    public string OnUpdate { get; init; } = "NO ACTION";
    public string OnDelete { get; init; } = "NO ACTION";

    public string CardinalitySymbol => Cardinality switch
    {
        ErdCardinality.OneToOne => "1 : 1",
        ErdCardinality.OneToMany => "1 : N",
        ErdCardinality.ManyToMany => "N : M",
        _ => "1 : N"
    };
}
