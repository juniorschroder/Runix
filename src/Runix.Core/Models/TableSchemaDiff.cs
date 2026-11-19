using System;
using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models;

/// <summary>
/// Representa a análise diferencial entre o estado original de uma tabela e o estado modificado.
/// </summary>
public class TableSchemaDiff
{
    public string OriginalTableName { get; set; } = string.Empty;
    public string TargetTableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";

    public bool IsNewTable { get; set; }
    public bool IsRenamed => !IsNewTable &&
                             !string.IsNullOrEmpty(OriginalTableName) &&
                             !string.IsNullOrEmpty(TargetTableName) &&
                             !string.Equals(OriginalTableName, TargetTableName, StringComparison.OrdinalIgnoreCase);

    public List<ColumnInfo> AddedColumns { get; set; } = new();
    public List<ColumnInfo> RemovedColumns { get; set; } = new();
    public List<(ColumnInfo OldCol, ColumnInfo NewCol)> AlteredColumns { get; set; } = new();

    public List<ForeignKeyDefinition> AddedForeignKeys { get; set; } = new();
    public List<ForeignKeyDefinition> RemovedForeignKeys { get; set; } = new();

    public List<ColumnInfo> OriginalColumns { get; set; } = new();
    public List<ColumnInfo> TargetColumns { get; set; } = new();

    public bool HasChanges => IsNewTable ||
                              IsRenamed ||
                              AddedColumns.Count > 0 ||
                              RemovedColumns.Count > 0 ||
                              AlteredColumns.Count > 0 ||
                              AddedForeignKeys.Count > 0 ||
                              RemovedForeignKeys.Count > 0;

    /// <summary>
    /// Calcula a diferença estrutural entre um nó original de tabela e o estado modificado.
    /// </summary>
    public static TableSchemaDiff Calculate(
        ErdTableNode? original,
        ErdTableNode modified,
        IReadOnlyList<ForeignKeyDefinition>? addedFks = null,
        IReadOnlyList<ForeignKeyDefinition>? removedFks = null)
    {
        var diff = new TableSchemaDiff
        {
            TargetTableName = modified.TableName,
            Schema = modified.Schema,
            TargetColumns = modified.Columns.ToList()
        };

        if (addedFks != null)
        {
            diff.AddedForeignKeys.AddRange(addedFks);
        }

        if (removedFks != null)
        {
            diff.RemovedForeignKeys.AddRange(removedFks);
        }

        if (original == null || string.IsNullOrWhiteSpace(original.TableName))
        {
            diff.IsNewTable = true;
            diff.AddedColumns.AddRange(modified.Columns);
            return diff;
        }

        diff.OriginalTableName = original.TableName;
        diff.OriginalColumns = original.Columns.ToList();

        var originalDict = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in original.Columns) originalDict[c.Name] = c;

        var modifiedDict = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in modified.Columns) modifiedDict[c.Name] = c;

        // Colunas Adicionadas
        foreach (var modCol in modified.Columns)
        {
            if (!originalDict.ContainsKey(modCol.Name))
            {
                diff.AddedColumns.Add(modCol);
            }
        }

        // Colunas Removidas
        foreach (var origCol in original.Columns)
        {
            if (!modifiedDict.ContainsKey(origCol.Name))
            {
                diff.RemovedColumns.Add(origCol);
            }
        }

        // Colunas Alteradas
        foreach (var modCol in modified.Columns)
        {
            if (originalDict.TryGetValue(modCol.Name, out var origCol))
            {
                var typeChanged = !string.Equals(origCol.DataType?.Trim(), modCol.DataType?.Trim(), StringComparison.OrdinalIgnoreCase);
                var pkChanged = origCol.IsPrimaryKey != modCol.IsPrimaryKey;
                var nullChanged = origCol.IsNullable != modCol.IsNullable;
                var defChanged = !string.Equals(origCol.DefaultValue?.Trim(), modCol.DefaultValue?.Trim(), StringComparison.OrdinalIgnoreCase);

                if (typeChanged || pkChanged || nullChanged || defChanged)
                {
                    diff.AlteredColumns.Add((origCol, modCol));
                }
            }
        }

        return diff;
    }
}
