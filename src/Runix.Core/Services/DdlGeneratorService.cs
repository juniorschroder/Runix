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
        var safeTable = $"\"{node.TableName.Replace("\"", "\"\"")}\"";

        sb.AppendLine($"CREATE TABLE {safeTable} (");

        var columnDefs = new List<string>();
        var pkCols = new List<string>();

        foreach (var col in node.Columns)
        {
            var safeCol = $"\"{col.Name.Replace("\"", "\"\"")}\"";
            var typeStr = MapDataType(col.DataType, type);
            var nullStr = col.IsNullable ? "" : " NOT NULL";
            var defStr = string.IsNullOrWhiteSpace(col.DefaultValue) ? "" : $" DEFAULT {col.DefaultValue}";

            columnDefs.Add($"    {safeCol} {typeStr}{nullStr}{defStr}");

            if (col.IsPrimaryKey)
            {
                pkCols.Add(safeCol);
            }
        }

        // Chave Primária
        if (pkCols.Count > 0)
        {
            columnDefs.Add($"    PRIMARY KEY ({string.Join(", ", pkCols)})");
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
            // PostgreSQL: Tabelas primeiro, depois ALTER TABLE para Foreign Keys
            foreach (var node in graph.Nodes)
            {
                sb.AppendLine(GenerateCreateTableSql(node, type));
            }

            if (graph.Relationships.Count > 0)
            {
                sb.AppendLine("-- Foreign Key Constraints");
                foreach (var rel in graph.Relationships)
                {
                    var safeSource = $"\"{rel.SourceTable.Replace("\"", "\"\"")}\"";
                    var safeTarget = $"\"{rel.TargetTable.Replace("\"", "\"\"")}\"";
                    var safeCol = $"\"{rel.SourceColumn.Replace("\"", "\"\"")}\"";
                    var safeTargetCol = $"\"{rel.TargetColumn.Replace("\"", "\"\"")}\"";
                    var constraintName = $"\"fk_{rel.SourceTable}_{rel.SourceColumn}\"";

                    sb.AppendLine($"ALTER TABLE {safeSource} ADD CONSTRAINT {constraintName} FOREIGN KEY ({safeCol}) REFERENCES {safeTarget}({safeTargetCol}) ON UPDATE {rel.OnUpdate} ON DELETE {rel.OnDelete};");
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
