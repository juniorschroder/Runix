using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Core.Services;

/// <summary>
/// Sintetizador de DDL relacional específico por dialeto para SQLite e PostgreSQL.
/// </summary>
public class DdlGeneratorService : IDdlGenerator
{
    public string GenerateCreateTableSql(ErdTableNode node, DatabaseType type)
    {
        var sb = new StringBuilder();
        var safeTable = type == DatabaseType.Oracle 
            ? $"\"{node.TableName.Replace("\"", "\"\"").ToUpperInvariant()}\""
            : $"\"{node.TableName.Replace("\"", "\"\"")}\"";

        sb.AppendLine($"CREATE TABLE {safeTable} (");

        var columnDefs = new List<string>();
        var pkCols = new List<string>();

        foreach (var col in node.Columns)
        {
            var safeCol = type == DatabaseType.Oracle
                ? $"\"{col.Name.Replace("\"", "\"\"").ToUpperInvariant()}\""
                : $"\"{col.Name.Replace("\"", "\"\"")}\"";
            var typeStr = MapDataType(col.DataType, type);
            var nullStr = col.IsNullable ? "" : " NOT NULL";
            var defVal = col.DefaultValue;
            if (type == DatabaseType.Oracle && !string.IsNullOrWhiteSpace(defVal))
            {
                if (string.Equals(defVal.Trim(), "true", StringComparison.OrdinalIgnoreCase)) defVal = "1";
                else if (string.Equals(defVal.Trim(), "false", StringComparison.OrdinalIgnoreCase)) defVal = "0";
            }
            var defStr = string.IsNullOrWhiteSpace(defVal) ? "" : $" DEFAULT {defVal}";

            columnDefs.Add($"    {safeCol} {typeStr}{nullStr}{defStr}");

            if (col.IsPrimaryKey)
            {
                pkCols.Add(safeCol);
            }
        }

        // Chave Primária
        if (pkCols.Count > 0)
        {
            if (type == DatabaseType.Oracle)
            {
                var pkConstraintName = $"\"PK_{node.TableName.Replace("\"", "\"\"").ToUpperInvariant()}\"";
                columnDefs.Add($"    CONSTRAINT {pkConstraintName} PRIMARY KEY ({string.Join(", ", pkCols)})");
            }
            else
            {
                columnDefs.Add($"    PRIMARY KEY ({string.Join(", ", pkCols)})");
            }
        }

        sb.Append(string.Join(",\n", columnDefs));
        sb.AppendLine("\n);");

        return sb.ToString();
    }

    public string GenerateFullSchemaSql(ErdGraphModel graph, DatabaseType type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- RUNIX Database Studio - Generated Schema ({type})");
        sb.AppendLine($"-- Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        if (type == DatabaseType.SQLite)
        {
            // No SQLite, as FKs são geradas idealmente inline dentro do CREATE TABLE
            foreach (var node in graph.Nodes)
            {
                var safeTable = $"\"{node.TableName.Replace("\"", "\"\"")}\"";
                sb.AppendLine($"CREATE TABLE {safeTable} (");

                var parts = new List<string>();
                var pkCols = new List<string>();

                foreach (var col in node.Columns)
                {
                    var safeCol = $"\"{col.Name.Replace("\"", "\"\"")}\"";
                    var typeStr = MapDataType(col.DataType, type);
                    var nullStr = col.IsNullable ? "" : " NOT NULL";
                    var defStr = string.IsNullOrWhiteSpace(col.DefaultValue) ? "" : $" DEFAULT {col.DefaultValue}";

                    parts.Add($"    {safeCol} {typeStr}{nullStr}{defStr}");
                    if (col.IsPrimaryKey) pkCols.Add(safeCol);
                }

                if (pkCols.Count > 0)
                {
                    parts.Add($"    PRIMARY KEY ({string.Join(", ", pkCols)})");
                }

                // FKs da tabela
                var nodeFks = graph.Relationships.Where(r => string.Equals(r.SourceTable, node.TableName, StringComparison.OrdinalIgnoreCase));
                foreach (var fk in nodeFks)
                {
                    var sourceCol = $"\"{fk.SourceColumn.Replace("\"", "\"\"")}\"";
                    var targetTable = $"\"{fk.TargetTable.Replace("\"", "\"\"")}\"";
                    var targetCol = $"\"{fk.TargetColumn.Replace("\"", "\"\"")}\"";

                    parts.Add($"    FOREIGN KEY ({sourceCol}) REFERENCES {targetTable}({targetCol}) ON UPDATE {fk.OnUpdate} ON DELETE {fk.OnDelete}");
                }

                sb.Append(string.Join(",\n", parts));
                sb.AppendLine("\n);\n");
            }
        }
        else
        {
            // PostgreSQL e Oracle: Tabelas primeiro, depois ALTER TABLE para Foreign Keys
            foreach (var node in graph.Nodes)
            {
                sb.AppendLine(GenerateCreateTableSql(node, type));
            }

            if (graph.Relationships.Count > 0)
            {
                sb.AppendLine("-- Foreign Key Constraints");
                foreach (var rel in graph.Relationships)
                {
                    var safeSource = type == DatabaseType.Oracle
                        ? $"\"{rel.SourceTable.Replace("\"", "\"\"").ToUpperInvariant()}\""
                        : $"\"{rel.SourceTable.Replace("\"", "\"\"")}\"";
                    var safeTarget = type == DatabaseType.Oracle
                        ? $"\"{rel.TargetTable.Replace("\"", "\"\"").ToUpperInvariant()}\""
                        : $"\"{rel.TargetTable.Replace("\"", "\"\"")}\"";
                    var safeCol = type == DatabaseType.Oracle
                        ? $"\"{rel.SourceColumn.Replace("\"", "\"\"").ToUpperInvariant()}\""
                        : $"\"{rel.SourceColumn.Replace("\"", "\"\"")}\"";
                    var safeTargetCol = type == DatabaseType.Oracle
                        ? $"\"{rel.TargetColumn.Replace("\"", "\"\"").ToUpperInvariant()}\""
                        : $"\"{rel.TargetColumn.Replace("\"", "\"\"")}\"";
                    var constraintName = type == DatabaseType.Oracle
                        ? $"\"FK_{rel.SourceTable.Replace("\"", "\"\"").ToUpperInvariant()}_{rel.SourceColumn.Replace("\"", "\"\"").ToUpperInvariant()}\""
                        : $"\"fk_{rel.SourceTable}_{rel.SourceColumn}\"";

                    if (type == DatabaseType.Oracle)
                    {
                        // Oracle NÃO suporta ON UPDATE em Foreign Keys
                        var deleteClause = rel.OnDelete?.Trim().ToUpperInvariant() switch
                        {
                            "CASCADE" => " ON DELETE CASCADE",
                            "SET NULL" => " ON DELETE SET NULL",
                            _ => ""
                        };
                        sb.AppendLine($"ALTER TABLE {safeSource} ADD CONSTRAINT {constraintName} FOREIGN KEY ({safeCol}) REFERENCES {safeTarget}({safeTargetCol}){deleteClause};");
                    }
                    else
                    {
                        sb.AppendLine($"ALTER TABLE {safeSource} ADD CONSTRAINT {constraintName} FOREIGN KEY ({safeCol}) REFERENCES {safeTarget}({safeTargetCol}) ON UPDATE {rel.OnUpdate} ON DELETE {rel.OnDelete};");
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static string MapDataType(string rawType, DatabaseType dbType)
    {
        var upper = rawType.ToUpperInvariant();
        if (dbType == DatabaseType.SQLite)
        {
            if (upper.Contains("INT")) return "INTEGER";
            if (upper.Contains("CHAR") || upper.Contains("TEXT") || upper.Contains("CLOB")) return "TEXT";
            if (upper.Contains("BLOB")) return "BLOB";
            if (upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB")) return "REAL";
            return "NUMERIC";
        }
        else if (dbType == DatabaseType.Oracle)
        {
            if (upper.Contains("BIGINT")) return "NUMBER(19)";
            if (upper.Contains("INT")) return "NUMBER(10)";
            if (upper.Contains("BOOLEAN") || upper.Equals("BOOL") || upper.Contains("BIT")) return "NUMBER(1)";
            if (upper.Contains("DECIMAL") || upper.Contains("NUMERIC") || upper.Contains("FLOAT") || upper.Contains("DOUBLE") || upper.Contains("REAL")) return "NUMBER(12,2)";
            if (upper.Contains("VARCHAR") || upper.Contains("CHAR")) return "VARCHAR2(255)";
            if (upper.Contains("TEXT") || upper.Contains("CLOB")) return "CLOB";
            if (upper.Contains("BLOB") || upper.Contains("BYTEA")) return "BLOB";
            if (upper.Contains("DATE")) return "DATE";
            if (upper.Contains("TIMESTAMP") || upper.Contains("TIME")) return "TIMESTAMP";
            if (upper.Contains("RAW") || upper.Contains("UUID") || upper.Contains("GUID")) return "RAW(16)";
            return upper;
        }
        else
        {
            if (upper.Contains("INTEGER") || upper.Equals("INT")) return "INTEGER";
            if (upper.Contains("BIGINT")) return "BIGINT";
            if (upper.Contains("VARCHAR")) return "VARCHAR(255)";
            if (upper.Contains("TEXT")) return "TEXT";
            if (upper.Contains("BOOLEAN") || upper.Equals("BOOL")) return "BOOLEAN";
            if (upper.Contains("TIMESTAMP")) return "TIMESTAMP";
            return upper;
        }
    }
}
