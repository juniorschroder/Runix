using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runix.Core.Models;
using Runix.Core.Models.Diff;

namespace Runix.Infrastructure.Diff;

/// <summary>
/// Gerador de scripts DDL de sincronização para PostgreSQL e SQLite respeitando diretrizes de segurança (Safe Mode).
/// </summary>
public static class SyncDdlGenerator
{
    public static string Generate(SchemaDiffResult diffResult, SyncOptions options)
    {
        if (diffResult == null || diffResult.Tables.Count == 0)
        {
            return "-- Nenhum objeto divergente encontrado para sincronizar.";
        }

        var sb = new StringBuilder();
        var targetDialect = options.TargetDialect;
        var isPg = targetDialect == DatabaseType.PostgreSQL;

        sb.AppendLine($"-- ===========================================================================");
        sb.AppendLine($"-- SCRIPT DE SINCRONIZAÇÃO DE SCHEMA (RUNIX DATABASE STUDIO)");
        sb.AppendLine($"-- Dialeto Alvo: {targetDialect}");
        sb.AppendLine($"-- Direção: {(options.Direction == SyncDirection.SourceToTarget ? "Origem ➔ Destino" : "Destino ➔ Origem")}");
        sb.AppendLine($"-- Modo Seguro (Sem DROPs): {(options.SafeMode ? "ATIVADO" : "DESATIVADO (Operações Destrutivas Permitidas)")}");
        sb.AppendLine($"-- Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"-- ===========================================================================");
        sb.AppendLine();

        if (isPg)
        {
            sb.AppendLine("BEGIN;");
            sb.AppendLine();
        }

        int statementsCount = 0;

        foreach (var table in diffResult.Tables)
        {
            if (!table.IsSelectedForSync || !options.IsTableSelected(table.Name))
            {
                continue;
            }

            if (table.Status == DiffStatus.Added)
            {
                // Find table in source snapshot
                var srcTable = diffResult.SourceSnapshot.Tables.FirstOrDefault(t => string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));
                if (srcTable == null) continue;

                sb.AppendLine($"-- [+ NOVO] Criar tabela '{srcTable.FullName}'");
                sb.AppendLine(GenerateCreateTable(srcTable, targetDialect));
                sb.AppendLine();
                statementsCount++;

                // Indexes on new table
                foreach (var idx in srcTable.Indexes.Where(i => !i.IsPrimaryKey))
                {
                    sb.AppendLine(GenerateCreateIndex(srcTable, idx, targetDialect));
                    statementsCount++;
                }
                sb.AppendLine();
            }
            else if (table.Status == DiffStatus.Modified)
            {
                var srcTable = diffResult.SourceSnapshot.Tables.FirstOrDefault(t => string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));
                var tgtTable = diffResult.TargetSnapshot.Tables.FirstOrDefault(t => string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));
                if (srcTable == null || tgtTable == null) continue;

                sb.AppendLine($"-- [~ MODIFICADO] Sincronizar tabela '{srcTable.FullName}'");

                if (isPg)
                {
                    GeneratePostgresTableAlterations(srcTable, table, options, sb, ref statementsCount);
                }
                else
                {
                    GenerateSqliteTableAlterations(srcTable, tgtTable, table, options, sb, ref statementsCount);
                }
                sb.AppendLine();
            }
            else if (table.Status == DiffStatus.Removed)
            {
                var tgtTable = diffResult.TargetSnapshot.Tables.FirstOrDefault(t => string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase));
                var fullTableName = tgtTable != null ? FormatTableName(tgtTable.Schema, tgtTable.Name, targetDialect) : table.Name;

                if (options.SafeMode)
                {
                    sb.AppendLine($"-- [- EXTRA] Tabela '{fullTableName}' omitida em Modo Seguro (DROP TABLE não executado).");
                }
                else
                {
                    sb.AppendLine($"-- [- EXTRA] Remover tabela '{fullTableName}'");
                    sb.AppendLine($"DROP TABLE {fullTableName};");
                    statementsCount++;
                }
                sb.AppendLine();
            }
        }

        if (isPg)
        {
            if (statementsCount > 0)
            {
                sb.AppendLine("COMMIT;");
            }
            else
            {
                sb.AppendLine("-- Nenhuma alteração executável gerada.");
                sb.AppendLine("ROLLBACK;");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void GeneratePostgresTableAlterations(TableSnapshot srcTable, TableDiffItem tableDiff, SyncOptions options, StringBuilder sb, ref int count)
    {
        var targetTable = FormatTableName(srcTable.Schema, srcTable.Name, DatabaseType.PostgreSQL);

        // 1. Column Additions
        foreach (var col in tableDiff.ColumnDiffs.Where(c => c.Status == DiffStatus.Added))
        {
            var srcCol = srcTable.Columns.FirstOrDefault(c => string.Equals(c.Name, col.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (srcCol == null) continue;

            var nullClause = srcCol.IsNullable ? "" : " NOT NULL";
            var defClause = !string.IsNullOrEmpty(srcCol.DefaultValue) ? $" DEFAULT {srcCol.DefaultValue}" : "";

            sb.AppendLine($"ALTER TABLE {targetTable} ADD COLUMN \"{srcCol.Name}\" {srcCol.DataType}{nullClause}{defClause};");
            count++;
        }

        // 2. Column Modifications (Types)
        foreach (var col in tableDiff.ColumnDiffs.Where(c => c.Status == DiffStatus.Modified))
        {
            var srcCol = srcTable.Columns.FirstOrDefault(c => string.Equals(c.Name, col.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (srcCol == null) continue;

            sb.AppendLine($"ALTER TABLE {targetTable} ALTER COLUMN \"{srcCol.Name}\" TYPE {srcCol.DataType};");
            count++;

            if (!srcCol.IsNullable)
            {
                sb.AppendLine($"ALTER TABLE {targetTable} ALTER COLUMN \"{srcCol.Name}\" SET NOT NULL;");
            }
            else
            {
                sb.AppendLine($"ALTER TABLE {targetTable} ALTER COLUMN \"{srcCol.Name}\" DROP NOT NULL;");
            }
            count++;
        }

        // 3. Column Removals
        foreach (var col in tableDiff.ColumnDiffs.Where(c => c.Status == DiffStatus.Removed))
        {
            if (options.SafeMode)
            {
                sb.AppendLine($"-- Omitido em Modo Seguro: ALTER TABLE {targetTable} DROP COLUMN \"{col.ColumnName}\";");
            }
            else
            {
                sb.AppendLine($"ALTER TABLE {targetTable} DROP COLUMN \"{col.ColumnName}\";");
                count++;
            }
        }

        // 4. Index Additions
        foreach (var idxDiff in tableDiff.IndexDiffs.Where(i => i.Status == DiffStatus.Added))
        {
            var srcIdx = srcTable.Indexes.FirstOrDefault(i => string.Equals(i.Name, idxDiff.IndexName, StringComparison.OrdinalIgnoreCase));
            if (srcIdx != null && !srcIdx.IsPrimaryKey)
            {
                sb.AppendLine(GenerateCreateIndex(srcTable, srcIdx, DatabaseType.PostgreSQL));
                count++;
            }
        }

        // 5. Index Removals
        foreach (var idxDiff in tableDiff.IndexDiffs.Where(i => i.Status == DiffStatus.Removed))
        {
            if (!options.SafeMode)
            {
                sb.AppendLine($"DROP INDEX IF EXISTS \"{idxDiff.IndexName}\";");
                count++;
            }
        }
    }

    private static void GenerateSqliteTableAlterations(TableSnapshot srcTable, TableSnapshot tgtTable, TableDiffItem tableDiff, SyncOptions options, StringBuilder sb, ref int count)
    {
        var targetTable = $"\"{srcTable.Name}\"";

        // Check if only simple column additions are present (which SQLite supports via ALTER TABLE ADD COLUMN)
        bool hasRemovals = tableDiff.ColumnDiffs.Any(c => c.Status == DiffStatus.Removed);
        bool hasModifications = tableDiff.ColumnDiffs.Any(c => c.Status == DiffStatus.Modified);

        if (!hasRemovals && !hasModifications)
        {
            // Pure column additions
            foreach (var col in tableDiff.ColumnDiffs.Where(c => c.Status == DiffStatus.Added))
            {
                var srcCol = srcTable.Columns.FirstOrDefault(c => string.Equals(c.Name, col.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (srcCol == null) continue;

                var defClause = !string.IsNullOrEmpty(srcCol.DefaultValue) ? $" DEFAULT {srcCol.DefaultValue}" : "";
                sb.AppendLine($"ALTER TABLE {targetTable} ADD COLUMN \"{srcCol.Name}\" {srcCol.DataType}{defClause};");
                count++;
            }
        }
        else
        {
            // Complex migration requiring temporary table rebuild
            var tempTable = $"\"_runix_new_{srcTable.Name}\"";
            sb.AppendLine($"-- Recriação segura temporária para SQLite (alteração de tipos/remoção)");

            // 1. Create temporary table with desired structure
            var createNewSql = GenerateCreateTable(srcTable, DatabaseType.SQLite).Replace(targetTable, tempTable);
            sb.AppendLine(createNewSql);

            // 2. Copy matching columns
            var commonCols = srcTable.Columns
                .Where(sc => tgtTable.Columns.Any(tc => string.Equals(tc.Name, sc.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(c => $"\"{c.Name}\"")
                .ToList();

            if (commonCols.Count > 0)
            {
                var colList = string.Join(", ", commonCols);
                sb.AppendLine($"INSERT INTO {tempTable} ({colList}) SELECT {colList} FROM {targetTable};");
            }

            // 3. Drop old & Rename new
            sb.AppendLine($"DROP TABLE {targetTable};");
            sb.AppendLine($"ALTER TABLE {tempTable} RENAME TO \"{srcTable.Name}\";");
            count += 4;
        }

        // Indexes
        foreach (var idxDiff in tableDiff.IndexDiffs.Where(i => i.Status == DiffStatus.Added))
        {
            var srcIdx = srcTable.Indexes.FirstOrDefault(i => string.Equals(i.Name, idxDiff.IndexName, StringComparison.OrdinalIgnoreCase));
            if (srcIdx != null && !srcIdx.IsPrimaryKey)
            {
                sb.AppendLine(GenerateCreateIndex(srcTable, srcIdx, DatabaseType.SQLite));
                count++;
            }
        }
    }

    private static string GenerateCreateTable(TableSnapshot table, DatabaseType dialect)
    {
        var sb = new StringBuilder();
        var fullTableName = FormatTableName(table.Schema, table.Name, dialect);

        sb.AppendLine($"CREATE TABLE {fullTableName} (");

        var colLines = new List<string>();
        foreach (var col in table.Columns)
        {
            var line = $"    \"{col.Name}\" {col.DataType}";
            if (col.IsPrimaryKey) line += " PRIMARY KEY";
            if (!col.IsNullable && !col.IsPrimaryKey) line += " NOT NULL";
            if (!string.IsNullOrEmpty(col.DefaultValue)) line += $" DEFAULT {col.DefaultValue}";
            colLines.Add(line);
        }

        sb.Append(string.Join(",\n", colLines));
        sb.Append("\n);");

        return sb.ToString();
    }

    private static string GenerateCreateIndex(TableSnapshot table, IndexSnapshot index, DatabaseType dialect)
    {
        var uniqueClause = index.IsUnique ? "UNIQUE " : "";
        var fullTableName = FormatTableName(table.Schema, table.Name, dialect);
        var colList = string.Join(", ", index.Columns.Select(c => $"\"{c}\""));

        return $"CREATE {uniqueClause}INDEX IF NOT EXISTS \"{index.Name}\" ON {fullTableName} ({colList});";
    }

    private static string FormatTableName(string schema, string table, DatabaseType dialect)
    {
        if (dialect == DatabaseType.PostgreSQL && !string.IsNullOrEmpty(schema))
        {
            return $"\"{schema}\".\"{table}\"";
        }
        return $"\"{table}\"";
    }
}
