using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Migrations;

/// <summary>
/// Gerador de instruções DDL específicas para o dialeto PostgreSQL.
/// </summary>
public class PostgresMigrationGenerator : ISqlDialectMigrationGenerator
{
    public DatabaseType SupportedType => DatabaseType.PostgreSQL;

    public string GenerateSql(MigrationPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- RUNIX DDL Migration Script (PostgreSQL)");
        sb.AppendLine($"-- Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("BEGIN;\n");

        foreach (var op in plan.Operations)
        {
            switch (op.Type)
            {
                case MigrationOperationType.CreateTable:
                    sb.AppendLine(GenerateCreateTableInternal(op));
                    break;

                case MigrationOperationType.DropTable:
                    sb.AppendLine($"DROP TABLE IF EXISTS {QuoteTable(op.Schema, op.TableName)} CASCADE;\n");
                    break;

                case MigrationOperationType.RenameTable:
                    if (!string.IsNullOrWhiteSpace(op.NewTableName))
                    {
                        sb.AppendLine($"ALTER TABLE {QuoteTable(op.Schema, op.TableName)} RENAME TO {QuoteIdent(op.NewTableName)};\n");
                    }
                    break;

                case MigrationOperationType.AddColumn:
                    if (op.Column != null)
                    {
                        var colDef = BuildColumnSql(op.Column);
                        sb.AppendLine($"ALTER TABLE {QuoteTable(op.Schema, op.TableName)} ADD COLUMN {colDef};\n");
                    }
                    break;

                case MigrationOperationType.DropColumn:
                    if (op.Column != null)
                    {
                        sb.AppendLine($"ALTER TABLE {QuoteTable(op.Schema, op.TableName)} DROP COLUMN {QuoteIdent(op.Column.Name)} CASCADE;\n");
                    }
                    break;

                case MigrationOperationType.AlterColumn:
                    if (op.Column != null && op.OldColumn != null)
                    {
                        sb.AppendLine(GenerateAlterColumnSql(op.Schema, op.TableName, op.OldColumn, op.Column));
                    }
                    break;

                case MigrationOperationType.AddForeignKey:
                    if (op.ForeignKey != null)
                    {
                        sb.AppendLine(GenerateAddForeignKey(op.ForeignKey));
                    }
                    break;

                case MigrationOperationType.DropForeignKey:
                    if (op.ForeignKey != null)
                    {
                        var constraint = string.IsNullOrWhiteSpace(op.ForeignKey.ConstraintName)
                            ? ForeignKeyDefinition.GenerateDefaultConstraintName(op.TableName, op.ForeignKey.SourceColumn)
                            : op.ForeignKey.ConstraintName;
                        sb.AppendLine($"ALTER TABLE {QuoteTable(op.Schema, op.TableName)} DROP CONSTRAINT IF EXISTS {QuoteIdent(constraint)};\n");
                    }
                    break;
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
            Schema = string.IsNullOrWhiteSpace(node.Schema) ? "public" : node.Schema,
            TargetColumns = node.Columns.ToList()
        };
        return GenerateCreateTableInternal(op);
    }

    public string GenerateAddForeignKey(ForeignKeyDefinition fk)
    {
        var constraintName = string.IsNullOrWhiteSpace(fk.ConstraintName)
            ? ForeignKeyDefinition.GenerateDefaultConstraintName(fk.SourceTable, fk.SourceColumn)
            : fk.ConstraintName;

        var sourceTable = QuoteIdent(fk.SourceTable);
        var sourceCol = QuoteIdent(fk.SourceColumn);
        var targetTable = QuoteIdent(fk.TargetTable);
        var targetCol = QuoteIdent(fk.TargetColumn);

        return $"ALTER TABLE {sourceTable} ADD CONSTRAINT {QuoteIdent(constraintName)} FOREIGN KEY ({sourceCol}) REFERENCES {targetTable}({targetCol}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete};\n";
    }

    private string GenerateCreateTableInternal(MigrationOperation op)
    {
        var sb = new StringBuilder();
        var tableQuoted = QuoteTable(op.Schema, op.TableName);

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

        // Adiciona FKs especificadas para a tabela
        foreach (var fk in op.TargetForeignKeys)
        {
            var constraintName = string.IsNullOrWhiteSpace(fk.ConstraintName)
                ? ForeignKeyDefinition.GenerateDefaultConstraintName(op.TableName, fk.SourceColumn)
                : fk.ConstraintName;
            var sourceCol = QuoteIdent(fk.SourceColumn);
            var targetTable = QuoteIdent(fk.TargetTable);
            var targetCol = QuoteIdent(fk.TargetColumn);

            lines.Add($"    CONSTRAINT {QuoteIdent(constraintName)} FOREIGN KEY ({sourceCol}) REFERENCES {targetTable}({targetCol}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete}");
        }

        sb.Append(string.Join(",\n", lines));
        sb.AppendLine("\n);\n");

        return sb.ToString();
    }

    private string GenerateAlterColumnSql(string schema, string tableName, ColumnInfo oldCol, ColumnInfo newCol)
    {
        var sb = new StringBuilder();
        var tableQuoted = QuoteTable(schema, tableName);
        var colQuoted = QuoteIdent(newCol.Name);

        // Alteração de tipo
        if (!string.Equals(oldCol.DataType?.Trim(), newCol.DataType?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var targetType = MapDataType(newCol.DataType ?? "TEXT");
            sb.AppendLine($"ALTER TABLE {tableQuoted} ALTER COLUMN {colQuoted} TYPE {targetType};");
        }

        // Alteração de nulabilidade
        if (oldCol.IsNullable != newCol.IsNullable)
        {
            var action = newCol.IsNullable ? "DROP NOT NULL" : "SET NOT NULL";
            sb.AppendLine($"ALTER TABLE {tableQuoted} ALTER COLUMN {colQuoted} {action};");
        }

        // Alteração de default
        if (!string.Equals(oldCol.DefaultValue?.Trim(), newCol.DefaultValue?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(newCol.DefaultValue))
            {
                sb.AppendLine($"ALTER TABLE {tableQuoted} ALTER COLUMN {colQuoted} DROP DEFAULT;");
            }
            else
            {
                sb.AppendLine($"ALTER TABLE {tableQuoted} ALTER COLUMN {colQuoted} SET DEFAULT {newCol.DefaultValue};");
            }
        }

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
        if (upper.Contains("INT8") || upper.Contains("BIGINT")) return "BIGINT";
        if (upper.Contains("INT4") || upper.Contains("INTEGER") || upper == "INT") return "INTEGER";
        if (upper.Contains("INT2") || upper.Contains("SMALLINT")) return "SMALLINT";
        if (upper.Contains("VARCHAR")) return upper.Contains("(") ? upper : "VARCHAR(255)";
        if (upper.Contains("CHAR")) return upper.Contains("(") ? upper : "CHAR(1)";
        if (upper.Contains("TEXT")) return "TEXT";
        if (upper.Contains("BOOL")) return "BOOLEAN";
        if (upper.Contains("TIMESTAMP")) return upper.Contains("WITH TIME ZONE") ? "TIMESTAMPTZ" : "TIMESTAMP";
        if (upper.Contains("DATE")) return "DATE";
        if (upper.Contains("TIME")) return "TIME";
        if (upper.Contains("FLOAT") || upper.Contains("DOUBLE") || upper.Contains("REAL")) return "DOUBLE PRECISION";
        if (upper.Contains("DECIMAL") || upper.Contains("NUMERIC")) return upper.Contains("(") ? upper : "NUMERIC(14,2)";
        if (upper.Contains("BYTEA") || upper.Contains("BLOB")) return "BYTEA";
        if (upper.Contains("JSONB")) return "JSONB";
        if (upper.Contains("JSON")) return "JSON";
        if (upper.Contains("UUID")) return "UUID";
        return upper;
    }

    private static string QuoteIdent(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static string QuoteTable(string? schema, string table)
    {
        if (string.IsNullOrWhiteSpace(schema) || schema.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            return QuoteIdent(table);
        }
        return $"{QuoteIdent(schema)}.{QuoteIdent(table)}";
    }
}
