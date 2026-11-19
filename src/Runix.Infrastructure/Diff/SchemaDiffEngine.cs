using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Models.Diff;

namespace Runix.Infrastructure.Diff;

/// <summary>
/// Motor de comparação profunda entre dois snapshots estruturais com normalização semântica cross-dialect (SQLite ↔ PostgreSQL) e mapeamento tolerante.
/// </summary>
public static class SchemaDiffEngine
{
    public static SchemaDiffResult Compare(SchemaSnapshotModel source, SchemaSnapshotModel target, bool invertDirection = false)
    {
        return Compare(source, target, new SchemaCompareOptions { InvertDirection = invertDirection });
    }

    public static SchemaDiffResult Compare(SchemaSnapshotModel source, SchemaSnapshotModel target, SchemaCompareOptions? options)
    {
        options ??= new SchemaCompareOptions();

        var src = options.InvertDirection ? target : source;
        var tgt = options.InvertDirection ? source : target;

        var result = new SchemaDiffResult
        {
            SourceSnapshot = src,
            TargetSnapshot = tgt
        };

        var srcTables = src.Tables
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var tgtTables = tgt.Tables
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var allTableNames = new HashSet<string>(srcTables.Keys, StringComparer.OrdinalIgnoreCase);
        allTableNames.UnionWith(tgtTables.Keys);

        foreach (var tableName in allTableNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var hasSrc = srcTables.TryGetValue(tableName, out var srcTable);
            var hasTgt = tgtTables.TryGetValue(tableName, out var tgtTable);

            if (hasSrc && !hasTgt)
            {
                // Table added in source
                var item = new TableDiffItem
                {
                    Name = srcTable!.Name,
                    Schema = srcTable.Schema,
                    Status = DiffStatus.Added,
                    IsSelectedForSync = true
                };

                foreach (var col in srcTable.Columns)
                {
                    item.ColumnDiffs.Add(new ColumnDiffItem
                    {
                        ColumnName = col.Name,
                        Status = DiffStatus.Added,
                        SourceType = col.DataType,
                        SourceNullable = col.IsNullable,
                        SourceDefault = col.DefaultValue,
                        DifferenceDescription = "Coluna nova na tabela a adicionar"
                    });
                }

                result.Tables.Add(item);
            }
            else if (!hasSrc && hasTgt)
            {
                // Table exists only in target (removed from source)
                var item = new TableDiffItem
                {
                    Name = tgtTable!.Name,
                    Schema = tgtTable.Schema,
                    Status = DiffStatus.Removed,
                    IsSelectedForSync = true
                };

                foreach (var col in tgtTable.Columns)
                {
                    item.ColumnDiffs.Add(new ColumnDiffItem
                    {
                        ColumnName = col.Name,
                        Status = DiffStatus.Removed,
                        TargetType = col.DataType,
                        TargetNullable = col.IsNullable,
                        TargetDefault = col.DefaultValue,
                        DifferenceDescription = "Tabela extra no destino"
                    });
                }

                result.Tables.Add(item);
            }
            else
            {
                // Table exists in both -> deep compare columns and indexes
                var item = CompareExistingTable(srcTable!, tgtTable!, options);
                result.Tables.Add(item);
            }
        }

        return result;
    }

    private static TableDiffItem CompareExistingTable(TableSnapshot src, TableSnapshot tgt, SchemaCompareOptions options)
    {
        var item = new TableDiffItem
        {
            Name = src.Name,
            Schema = src.Schema,
            Status = DiffStatus.Identical,
            IsSelectedForSync = true
        };

        var srcCols = src.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var tgtCols = tgt.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var allColNames = new HashSet<string>(srcCols.Keys, StringComparer.OrdinalIgnoreCase);
        allColNames.UnionWith(tgtCols.Keys);

        bool hasColDifferences = false;

        foreach (var colName in allColNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var hasSrcCol = srcCols.TryGetValue(colName, out var sc);
            var hasTgtCol = tgtCols.TryGetValue(colName, out var tc);

            if (hasSrcCol && !hasTgtCol)
            {
                hasColDifferences = true;
                item.ColumnDiffs.Add(new ColumnDiffItem
                {
                    ColumnName = sc!.Name,
                    Status = DiffStatus.Added,
                    SourceType = sc.DataType,
                    SourceNullable = sc.IsNullable,
                    SourceDefault = sc.DefaultValue,
                    DifferenceDescription = "Coluna ausente no Destino"
                });
            }
            else if (!hasSrcCol && hasTgtCol)
            {
                hasColDifferences = true;
                item.ColumnDiffs.Add(new ColumnDiffItem
                {
                    ColumnName = tc!.Name,
                    Status = DiffStatus.Removed,
                    TargetType = tc.DataType,
                    TargetNullable = tc.IsNullable,
                    TargetDefault = tc.DefaultValue,
                    DifferenceDescription = "Coluna extra no Destino"
                });
            }
            else
            {
                // Both have column -> compare properties
                bool typeDiff;
                bool isCompatible = false;
                string dialectNote = string.Empty;

                if (options.StrictTypeMatching)
                {
                    // Strict mode: exact normalized data type comparison (case-insensitive, trimmed)
                    var cleanSrc = CleanType(sc.DataType);
                    var cleanTgt = CleanType(tc.DataType);
                    typeDiff = !string.Equals(cleanSrc, cleanTgt, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // Tolerant mode: evaluate cross-dialect equivalence and affinities
                    var normSrcType = NormalizeType(sc.DataType);
                    var normTgtType = NormalizeType(tc.DataType);

                    if (string.Equals(normSrcType, normTgtType, StringComparison.OrdinalIgnoreCase))
                    {
                        typeDiff = false;
                        if (!string.Equals(sc.DataType?.Trim(), tc.DataType?.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            isCompatible = true;
                            dialectNote = $"Compatível entre dialetos: {sc.DataType} ↔ {tc.DataType}";
                        }
                    }
                    else if (IsCrossDialectEquivalent(sc.DataType, tc.DataType, out dialectNote))
                    {
                        typeDiff = false;
                        isCompatible = true;
                    }
                    else
                    {
                        typeDiff = true;
                    }
                }

                bool nullDiff = sc.IsNullable != tc.IsNullable;
                bool pkDiff = sc.IsPrimaryKey != tc.IsPrimaryKey;

                if (typeDiff || nullDiff || pkDiff)
                {
                    hasColDifferences = true;
                    var reasons = new List<string>();
                    if (typeDiff) reasons.Add($"Tipo: {sc.DataType} vs {tc.DataType}");
                    if (nullDiff) reasons.Add($"Nulável: {sc.IsNullable} vs {tc.IsNullable}");
                    if (pkDiff) reasons.Add($"PK: {sc.IsPrimaryKey} vs {tc.IsPrimaryKey}");

                    item.ColumnDiffs.Add(new ColumnDiffItem
                    {
                        ColumnName = sc.Name,
                        Status = DiffStatus.Modified,
                        SourceType = sc.DataType,
                        TargetType = tc.DataType,
                        SourceNullable = sc.IsNullable,
                        TargetNullable = tc.IsNullable,
                        SourceDefault = sc.DefaultValue,
                        TargetDefault = tc.DefaultValue,
                        IsCrossDialectCompatible = isCompatible,
                        DialectNote = isCompatible ? dialectNote : null,
                        DifferenceDescription = string.Join("; ", reasons)
                    });
                }
                else
                {
                    item.ColumnDiffs.Add(new ColumnDiffItem
                    {
                        ColumnName = sc.Name,
                        Status = DiffStatus.Identical,
                        SourceType = sc.DataType,
                        TargetType = tc.DataType,
                        SourceNullable = sc.IsNullable,
                        TargetNullable = tc.IsNullable,
                        SourceDefault = sc.DefaultValue,
                        TargetDefault = tc.DefaultValue,
                        IsCrossDialectCompatible = isCompatible,
                        DialectNote = isCompatible ? dialectNote : null,
                        DifferenceDescription = isCompatible ? dialectNote : "Idêntica"
                    });
                }
            }
        }

        // Compare Indexes (unless ignored by options)
        bool hasIdxDifferences = false;
        if (!options.IgnoreIndexes)
        {
            var srcIdx = src.Indexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
            var tgtIdx = tgt.Indexes.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var idx in src.Indexes)
            {
                if (!tgtIdx.ContainsKey(idx.Name))
                {
                    hasIdxDifferences = true;
                    item.IndexDiffs.Add(new IndexDiffItem
                    {
                        IndexName = idx.Name,
                        Status = DiffStatus.Added,
                        DifferenceDescription = $"Índice ausente no Destino ({string.Join(", ", idx.Columns)})"
                    });
                }
            }

            foreach (var idx in tgt.Indexes)
            {
                if (!srcIdx.ContainsKey(idx.Name))
                {
                    hasIdxDifferences = true;
                    item.IndexDiffs.Add(new IndexDiffItem
                    {
                        IndexName = idx.Name,
                        Status = DiffStatus.Removed,
                        DifferenceDescription = $"Índice extra no Destino ({string.Join(", ", idx.Columns)})"
                    });
                }
            }
        }

        if (hasColDifferences || hasIdxDifferences)
        {
            item.Status = DiffStatus.Modified;
        }
        else
        {
            item.Status = DiffStatus.Identical;
            item.IsSelectedForSync = false;
        }

        return item;
    }

    private static string CleanType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;
        return type.Trim().ToLowerInvariant();
    }

    public static string NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "text";
        var t = type.Trim().ToLowerInvariant();

        var parenIdx = t.IndexOf('(');
        if (parenIdx > 0)
        {
            t = t.Substring(0, parenIdx).Trim();
        }

        return t switch
        {
            "int4" or "int" or "integer" or "serial" => "integer",
            "int8" or "bigint" or "bigserial" => "bigint",
            "int2" or "smallint" or "smallserial" => "smallint",
            "bool" or "boolean" => "boolean",
            "varchar" or "character varying" or "char" or "character" or "text" or "clob" => "text",
            "float4" or "real" => "real",
            "float8" or "double precision" or "double" => "double precision",
            "numeric" or "decimal" => "numeric",
            "bytea" or "blob" or "binary" or "varbinary" => "blob",
            "timestamp" or "timestamptz" or "timestamp without time zone" or "timestamp with time zone" or "datetime" => "timestamp",
            _ => t
        };
    }

    /// <summary>
    /// Avalia se dois tipos de dados são semanticamente compatíveis entre diferentes dialetos (ex: SQLite ↔ PostgreSQL).
    /// </summary>
    public static bool IsCrossDialectEquivalent(string? typeA, string? typeB, out string note)
    {
        var normA = NormalizeType(typeA);
        var normB = NormalizeType(typeB);

        if (string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase))
        {
            note = "Idêntico após normalização";
            return true;
        }

        var famA = GetAffinityFamily(normA);
        var famB = GetAffinityFamily(normB);

        if (famA != AffinityFamily.Unknown && famA == famB)
        {
            note = $"Compatível entre dialetos ({famA}): {typeA} ↔ {typeB}";
            return true;
        }

        note = string.Empty;
        return false;
    }

    private static AffinityFamily GetAffinityFamily(string normalizedType)
    {
        return normalizedType switch
        {
            "integer" or "bigint" or "smallint" or "boolean" => AffinityFamily.IntegerNumeric,
            "text" => AffinityFamily.Textual,
            "numeric" or "real" or "double precision" => AffinityFamily.FloatingDecimal,
            "blob" => AffinityFamily.BinaryBlob,
            "timestamp" or "date" or "time" => AffinityFamily.DateTime,
            _ => AffinityFamily.Unknown
        };
    }

    private enum AffinityFamily
    {
        Unknown,
        IntegerNumeric,
        Textual,
        FloatingDecimal,
        BinaryBlob,
        DateTime
    }
}
