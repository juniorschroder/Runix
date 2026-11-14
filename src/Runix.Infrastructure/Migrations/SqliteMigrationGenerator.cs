using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Migrations;

/// <summary>
/// Gerador de instruções DDL específicas para o dialeto SQLite, com suporte ao padrão seguro de recriação de tabelas.
/// </summary>
public class SqliteMigrationGenerator : ISqlDialectMigrationGenerator
{
    public DatabaseType SupportedType => DatabaseType.SQLite;

    public string GenerateSql(MigrationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- RUNIX DDL Migration Script (SQLite)");
        sb.AppendLine($"-- Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Se houver operação de RebuildTableSqlite ou o plano exigir reconstrução
        var rebuildOp = plan.Operations.FirstOrDefault(o => o.Type == MigrationOperationType.RebuildTableSqlite);
        if (rebuildOp != null || plan.RequiresTableRebuild)
        {
            var op = rebuildOp ?? plan.Operations.First();
            sb.Append(GenerateSafeRebuildScript(op));
            return sb.ToString();
        }

        // Operações nativas diretas
        sb.AppendLine("BEGIN TRANSACTION;\n");

        foreach (var op in plan.Operations)
        {
            switch (op.Type)
            {
                case MigrationOperationType.CreateTable:
                    sb.AppendLine(GenerateCreateTableInternal(op));
                    break;

                case MigrationOperationType.DropTable:
                    sb.AppendLine($"DROP TABLE IF EXISTS {QuoteIdent(op.TableName)};\n");
                    break;

                case MigrationOperationType.RenameTable:
                    if (!string.IsNullOrWhiteSpace(op.NewTableName))
                    {
                        sb.AppendLine($"ALTER TABLE {QuoteIdent(op.TableName)} RENAME TO {QuoteIdent(op.NewTableName)};\n");
                    }
                    break;

                case MigrationOperationType.AddColumn:
                    if (op.Column != null)
                    {
                        var colDef = BuildColumnSql(op.Column);
                        sb.AppendLine($"ALTER TABLE {QuoteIdent(op.TableName)} ADD COLUMN {colDef};\n");
                    }
                    break;

                case MigrationOperationType.AddForeignKey:
                    // Adição de FK isolada no SQLite requer recriação de tabela
                    var rebuildWithFk = new MigrationOperation
                    {
                        Type = MigrationOperationType.RebuildTableSqlite,
                        TableName = op.TableName,
                        ExistingColumns = op.ExistingColumns,
                        TargetColumns = op.TargetColumns.Count > 0 ? op.TargetColumns : op.ExistingColumns,
                        TargetForeignKeys = op.ForeignKey != null ? new List<ForeignKeyDefinition> { op.ForeignKey } : new()
                    };
                    return GenerateSafeRebuildScript(rebuildWithFk);
            }
        }

        sb.AppendLine("COMMIT;");
        return sb.ToString();
    }

    public string GenerateCreateTable(ErdTableNode node)
    {
        var op = new MigrationOperation
        {
            Type = MigrationOperationType.CreateTable,
            TableName = node.TableName,
            TargetColumns = node.Columns.ToList()
        };
        return GenerateCreateTableInternal(op);
    }

    public string GenerateAddForeignKey(ForeignKeyDefinition fk)
    {
        // No SQLite, adicionar FK requer a reconstrução da tabela
        var op = new MigrationOperation
        {
            Type = MigrationOperationType.RebuildTableSqlite,
            TableName = fk.SourceTable,
            TargetForeignKeys = new List<ForeignKeyDefinition> { fk }
        };
        return GenerateSafeRebuildScript(op);
    }

    /// <summary>
    /// Gera o padrão seguro de 10 passos para reconstrução de tabela no SQLite com preservação de dados.
    /// </summary>
    public string GenerateSafeRebuildScript(MigrationOperation op)
    {
        var sb = new StringBuilder();
        var origTable = op.TableName;
        var targetTable = string.IsNullOrWhiteSpace(op.NewTableName) ? origTable : op.NewTableName;
        var tempTable = $"{origTable}_runix_new";

        sb.AppendLine("PRAGMA foreign_keys = OFF;");
        sb.AppendLine("BEGIN TRANSACTION;\n");

        // 1. Cria nova tabela temporária com o schema alvo
        sb.AppendLine($"-- 1. Criar nova tabela temporária com a estrutura atualizada");
        sb.AppendLine($"CREATE TABLE {QuoteIdent(tempTable)} (");

        var colDefs = new List<string>();
        var pkCols = new List<string>();

        foreach (var col in op.TargetColumns)
        {
            colDefs.Add($"    {BuildColumnSql(col)}");
            if (col.IsPrimaryKey)
            {
                pkCols.Add(QuoteIdent(col.Name));
            }
        }

        if (pkCols.Count > 0)
        {
            colDefs.Add($"    PRIMARY KEY ({string.Join(", ", pkCols)})");
        }

        // Adiciona FKs inline
        foreach (var fk in op.TargetForeignKeys)
        {
            var sourceCol = QuoteIdent(fk.SourceColumn);
            var foreignTable = QuoteIdent(fk.TargetTable);
            var foreignCol = QuoteIdent(fk.TargetColumn);
            colDefs.Add($"    FOREIGN KEY ({sourceCol}) REFERENCES {foreignTable}({foreignCol}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete}");
        }

        sb.Append(string.Join(",\n", colDefs));
        sb.AppendLine("\n);\n");

        // 2. Copia os dados existentes para colunas compatíveis
        var commonCols = op.ExistingColumns
            .Where(ec => op.TargetColumns.Any(tc => string.Equals(tc.Name, ec.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(c => QuoteIdent(c.Name))
            .ToList();

        if (commonCols.Count > 0)
        {
            var colsJoined = string.Join(", ", commonCols);
            sb.AppendLine($"-- 2. Migrar dados existentes para a nova tabela");
            sb.AppendLine($"INSERT INTO {QuoteIdent(tempTable)} ({colsJoined})");
            sb.AppendLine($"SELECT {colsJoined} FROM {QuoteIdent(origTable)};\n");
        }

        // 3. Descarta tabela antiga
        sb.AppendLine($"-- 3. Remover tabela legada");
        sb.AppendLine($"DROP TABLE {QuoteIdent(origTable)};\n");

        // 4. Renomeia tabela temporária para o nome final
        sb.AppendLine($"-- 4. Renomear tabela temporária");
        sb.AppendLine($"ALTER TABLE {QuoteIdent(tempTable)} RENAME TO {QuoteIdent(targetTable)};\n");

        // 5. Valida integridade referencial e finaliza
        sb.AppendLine("-- 5. Validar integridade e commitar");
        sb.AppendLine("PRAGMA foreign_key_check;");
        sb.AppendLine("COMMIT;");
        sb.AppendLine("PRAGMA foreign_keys = ON;");

        return sb.ToString();
    }

    private string GenerateCreateTableInternal(MigrationOperation op)
    {
        var sb = new StringBuilder();
        var tableQuoted = QuoteIdent(op.TableName);

        sb.AppendLine($"CREATE TABLE {tableQuoted} (");

        var lines = new List<string>();
        var pkCols = new List<string>();

        foreach (var col in op.TargetColumns)
        {
            lines.Add($"    {BuildColumnSql(col)}");
            if (col.IsPrimaryKey)
            {
                pkCols.Add(QuoteIdent(col.Name));
            }
        }

        if (pkCols.Count > 0)
        {
            lines.Add($"    PRIMARY KEY ({string.Join(", ", pkCols)})");
        }

        // FKs inline
        foreach (var fk in op.TargetForeignKeys)
        {
            var sourceCol = QuoteIdent(fk.SourceColumn);
            var targetTable = QuoteIdent(fk.TargetTable);
            var targetCol = QuoteIdent(fk.TargetColumn);

            lines.Add($"    FOREIGN KEY ({sourceCol}) REFERENCES {targetTable}({targetCol}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete}");
        }

        sb.Append(string.Join(",\n", lines));
        sb.AppendLine("\n);\n");

        return sb.ToString();
    }

    private static string BuildColumnSql(ColumnInfo col)
    {
        var colQuoted = QuoteIdent(col.Name);
        var typeStr = MapDataType(col.DataType ?? "TEXT");
        var nullStr = col.IsNullable ? "" : " NOT NULL";
        var defStr = string.IsNullOrWhiteSpace(col.DefaultValue) ? "" : $" DEFAULT {col.DefaultValue}";

        return $"{colQuoted} {typeStr}{nullStr}{defStr}";
    }

    private static string MapDataType(string rawType)
    {
        var upper = rawType.Trim().ToUpperInvariant();
        if (upper.Contains("INT")) return "INTEGER";
        if (upper.Contains("CHAR") || upper.Contains("TEXT") || upper.Contains("CLOB") || upper.Contains("VARCHAR")) return "TEXT";
        if (upper.Contains("BLOB")) return "BLOB";
        if (upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB")) return "REAL";
        if (upper.Contains("BOOL")) return "INTEGER"; // SQLite armazena booleanos como 0/1
        return "NUMERIC";
    }

    private static string QuoteIdent(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
