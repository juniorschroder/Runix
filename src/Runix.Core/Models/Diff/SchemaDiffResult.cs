using System;
using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Resultado detalhado da comparação estrutural entre dois snapshots de schemas.
/// </summary>
public class SchemaDiffResult
{
    public SchemaSnapshotModel SourceSnapshot { get; set; } = new();
    public SchemaSnapshotModel TargetSnapshot { get; set; } = new();
    public List<TableDiffItem> Tables { get; set; } = new();

    public int AddedCount => Tables.Count(t => t.Status == DiffStatus.Added);
    public int ModifiedCount => Tables.Count(t => t.Status == DiffStatus.Modified);
    public int RemovedCount => Tables.Count(t => t.Status == DiffStatus.Removed);
    public int IdenticalCount => Tables.Count(t => t.Status == DiffStatus.Identical);

    public bool HasDifferences => AddedCount > 0 || ModifiedCount > 0 || RemovedCount > 0;

    public string Summary => $"{AddedCount} adicionada(s), {ModifiedCount} modificada(s), {RemovedCount} removida(s), {IdenticalCount} idêntica(s)";
}

public class TableDiffItem
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public DiffStatus Status { get; set; } = DiffStatus.Identical;
    public bool IsSelectedForSync { get; set; } = true;
    public List<ColumnDiffItem> ColumnDiffs { get; set; } = new();
    public List<IndexDiffItem> IndexDiffs { get; set; } = new();

    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";

    public string StatusBadge => Status switch
    {
        DiffStatus.Added => "[+ Novo]",
        DiffStatus.Modified => "[~ Modificado]",
        DiffStatus.Removed => "[- Extra]",
        _ => "[= Idêntico]"
    };

    public string StatusColor => Status switch
    {
        DiffStatus.Added => "#34d399",
        DiffStatus.Modified => "#fbbf24",
        DiffStatus.Removed => "#f87171",
        _ => "#8b9cb3"
    };

    public string Summary
    {
        get
        {
            if (Status == DiffStatus.Added) return "Tabela ausente no Destino";
            if (Status == DiffStatus.Removed) return "Tabela extra no Destino";
            if (Status == DiffStatus.Identical) return "Estrutura idêntica";

            var colMods = ColumnDiffs.Count(c => c.Status != DiffStatus.Identical);
            var idxMods = IndexDiffs.Count(i => i.Status != DiffStatus.Identical);
            return $"{colMods} coluna(s), {idxMods} índice(s) divergente(s)";
        }
    }
}

public class ColumnDiffItem
{
    public string ColumnName { get; set; } = string.Empty;
    public DiffStatus Status { get; set; } = DiffStatus.Identical;
    public string? SourceType { get; set; }
    public string? TargetType { get; set; }
    public bool? SourceNullable { get; set; }
    public bool? TargetNullable { get; set; }
    public string? SourceDefault { get; set; }
    public string? TargetDefault { get; set; }
    public string DifferenceDescription { get; set; } = string.Empty;
    public bool IsCrossDialectCompatible { get; set; }
    public string? DialectNote { get; set; }

    public string StatusBadge => Status switch
    {
        DiffStatus.Added => "[+ Adicionar]",
        DiffStatus.Modified => "[~ Alterar]",
        DiffStatus.Removed => "[- Remover]",
        _ => "[= Idêntico]"
    };

    public string StatusColor => Status switch
    {
        DiffStatus.Added => "#34d399",
        DiffStatus.Modified => "#fbbf24",
        DiffStatus.Removed => "#f87171",
        _ => "#8b9cb3"
    };
}

public class IndexDiffItem
{
    public string IndexName { get; set; } = string.Empty;
    public DiffStatus Status { get; set; } = DiffStatus.Identical;
    public string DifferenceDescription { get; set; } = string.Empty;
    public bool IsCrossDialectCompatible { get; set; }
    public string? DialectNote { get; set; }

    public string StatusBadge => Status switch
    {
        DiffStatus.Added => "[+ Adicionar]",
        DiffStatus.Modified => "[~ Alterar]",
        DiffStatus.Removed => "[- Remover]",
        _ => "[= Idêntico]"
    };

    public string StatusColor => Status switch
    {
        DiffStatus.Added => "#34d399",
        DiffStatus.Modified => "#fbbf24",
        DiffStatus.Removed => "#f87171",
        _ => "#8b9cb3"
    };
}
