using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.Diff;

namespace Runix.Infrastructure.Diff;

/// <summary>
/// Serviço unificado de extração de snapshots estruturais, comparação diferencial e execução de DDL.
/// </summary>
public class SchemaDiffService : ISchemaDiffService
{
    public static SchemaDiffService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex SqliteCheckRegex = new(
        @"(?i)(?:CONSTRAINT\s+[""`]?([a-zA-Z0-9_]+)[""`]?\s+)?CHECK\s*\(((?>[^()]+|\((?<DEPTH>)|\)(?<-DEPTH>))*(?(DEPTH)(?!)))\)",
        RegexOptions.Compiled);

    private static readonly Regex TriggerTimingRegex = new(
        @"(?i)\b(BEFORE|AFTER|INSTEAD\s+OF)\b",
        RegexOptions.Compiled);

    private static readonly Regex TriggerEventRegex = new(
        @"(?i)\b(INSERT|UPDATE|DELETE)\b",
        RegexOptions.Compiled);

    public async Task<SchemaSnapshotModel> ExtractSnapshotAsync(IDatabaseService db, SnapshotExtractionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        options ??= new SnapshotExtractionOptions();

        var snapshot = new SchemaSnapshotModel
        {
            Version = 1,
            DatabaseName = db.Config.Type == DatabaseType.SQLite 
                ? System.IO.Path.GetFileName(db.Config.FilePath ?? "database.db") 
                : (db.Config.Database ?? "postgres"),
            EngineType = db.Config.Type.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        // 1. Tables introspection
        if (options.IncludeTables)
        {
            var tables = await db.GetTablesAsync();

            foreach (var tbl in tables)
            {
                var tableSnap = new TableSnapshot
                {
                    Name = tbl.Name,
                    Schema = tbl.Schema
                };

                // Columns
                try
                {
                    var cols = await db.GetTableColumnsAsync(tbl.Name);
                    foreach (var c in cols)
                    {
                        tableSnap.Columns.Add(new ColumnSnapshot
                        {
                            Name = c.Name,
                            DataType = c.DataType,
                            IsNullable = c.IsNullable,
                            IsPrimaryKey = c.IsPrimaryKey,
                            DefaultValue = c.DefaultValue,
                            OrdinalPosition = c.OrdinalPosition
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching columns for {tbl.Name}: {ex.Message}");
                }

                // Indexes
                if (options.IncludeIndexes)
                {
                    try
                    {
                        var indexes = await db.GetTableIndexesAsync(tbl.Name);
                        foreach (var idx in indexes)
                        {
                            tableSnap.Indexes.Add(new IndexSnapshot
                            {
                                Name = idx.Name,
                                IsUnique = idx.IsUnique,
                                IsPrimaryKey = idx.IsPrimaryKey,
                                Columns = idx.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching indexes for {tbl.Name}: {ex.Message}");
                    }
                }

                // Foreign Keys
                if (options.IncludeForeignKeys)
                {
                    try
                    {
                        var fks = await db.GetForeignKeysAsync(tbl.Name);
                        foreach (var fk in fks)
                        {
                            tableSnap.ForeignKeys.Add(new ForeignKeySnapshot
                            {
                                ConstraintName = fk.ConstraintName,
                                ColumnName = fk.SourceColumn,
                                ForeignTableName = fk.TargetTable,
                                ForeignColumnName = fk.TargetColumn,
                                OnUpdate = fk.OnUpdate,
                                OnDelete = fk.OnDelete
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching foreign keys for {tbl.Name}: {ex.Message}");
                    }
                }

                // Check Constraints
                if (options.IncludeCheckConstraints)
                {
                    try
                    {
                        var checkConstraints = await ExtractCheckConstraintsAsync(db, tbl.Name);
                        tableSnap.CheckConstraints.AddRange(checkConstraints);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching check constraints for {tbl.Name}: {ex.Message}");
                    }
                }

                snapshot.Tables.Add(tableSnap);
            }
        }

        // 2. Views introspection
        if (options.IncludeViews)
        {
            try
            {
                var views = await ExtractViewsAsync(db);
                snapshot.Views.AddRange(views);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching views: {ex.Message}");
            }
        }

        // 3. Triggers introspection
        if (options.IncludeTriggers)
        {
            try
            {
                var triggers = await ExtractTriggersAsync(db);
                snapshot.Triggers.AddRange(triggers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching triggers: {ex.Message}");
            }
        }

        return snapshot;
    }

    private async Task<List<CheckConstraintSnapshot>> ExtractCheckConstraintsAsync(IDatabaseService db, string tableName)
    {
        var list = new List<CheckConstraintSnapshot>();

        if (db.Config.Type == DatabaseType.SQLite)
        {
            string safeTable = tableName.Replace("'", "''");
            var result = await db.ExecuteQueryAsync($"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{safeTable}';");
            if (result.Success && result.Rows.Count > 0 && result.Rows[0].Count > 0)
            {
                string ddl = result.Rows[0][0]?.ToString() ?? string.Empty;
                var matches = SqliteCheckRegex.Matches(ddl);
                int checkIndex = 1;
                foreach (Match m in matches)
                {
                    string name = m.Groups[1].Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value)
                        ? m.Groups[1].Value
                        : $"chk_{tableName}_{checkIndex++}";
                    string expression = m.Groups[2].Value.Trim();

                    list.Add(new CheckConstraintSnapshot
                    {
                        Name = name,
                        Expression = expression
                    });
                }
            }
        }
        else if (db.Config.Type == DatabaseType.PostgreSQL)
        {
            string safeTable = tableName.Replace("'", "''");
            string query = $@"
                SELECT con.conname AS constraint_name,
                       pg_get_constraintdef(con.oid, true) AS check_clause
                FROM pg_constraint con
                JOIN pg_class c ON con.conrelid = c.oid
                JOIN pg_namespace n ON c.relnamespace = n.oid
                WHERE con.contype = 'c'
                  AND c.relname = '{safeTable}'
                  AND n.nspname NOT IN ('pg_catalog', 'information_schema');";

            var result = await db.ExecuteQueryAsync(query);
            if (result.Success && result.Rows.Count > 0)
            {
                int nameCol = GetColumnIndex(result, "constraint_name");
                int clauseCol = GetColumnIndex(result, "check_clause");

                foreach (var row in result.Rows)
                {
                    string name = GetString(row, nameCol);
                    string clause = GetString(row, clauseCol);

                    string expression = clause;
                    if (expression.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
                    {
                        expression = expression.Substring(5).Trim();
                        if (expression.StartsWith('(') && expression.EndsWith(')'))
                        {
                            expression = expression.Substring(1, expression.Length - 2).Trim();
                        }
                    }

                    list.Add(new CheckConstraintSnapshot
                    {
                        Name = name,
                        Expression = expression
                    });
                }
            }
        }

        return list;
    }

    private async Task<List<ViewSnapshot>> ExtractViewsAsync(IDatabaseService db)
    {
        var list = new List<ViewSnapshot>();

        if (db.Config.Type == DatabaseType.SQLite)
        {
            var result = await db.ExecuteQueryAsync("SELECT name, sql FROM sqlite_master WHERE type = 'view' AND name NOT LIKE 'sqlite_%';");
            if (result.Success)
            {
                int nameCol = GetColumnIndex(result, "name");
                int sqlCol = GetColumnIndex(result, "sql");

                foreach (var row in result.Rows)
                {
                    string name = GetString(row, nameCol);
                    string sql = GetString(row, sqlCol);

                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(new ViewSnapshot
                        {
                            Name = name,
                            Schema = "main",
                            DefinitionDdl = sql
                        });
                    }
                }
            }
        }
        else if (db.Config.Type == DatabaseType.PostgreSQL)
        {
            string query = @"
                SELECT schemaname, viewname, definition
                FROM pg_views
                WHERE schemaname NOT IN ('pg_catalog', 'information_schema');";

            var result = await db.ExecuteQueryAsync(query);
            if (result.Success)
            {
                int schemaCol = GetColumnIndex(result, "schemaname");
                int nameCol = GetColumnIndex(result, "viewname");
                int defCol = GetColumnIndex(result, "definition");

                foreach (var row in result.Rows)
                {
                    string schema = GetString(row, schemaCol);
                    string name = GetString(row, nameCol);
                    string def = GetString(row, defCol);

                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(new ViewSnapshot
                        {
                            Name = name,
                            Schema = schema,
                            DefinitionDdl = def
                        });
                    }
                }
            }
        }

        return list;
    }

    private async Task<List<TriggerSnapshot>> ExtractTriggersAsync(IDatabaseService db)
    {
        var list = new List<TriggerSnapshot>();

        if (db.Config.Type == DatabaseType.SQLite)
        {
            var result = await db.ExecuteQueryAsync("SELECT name, tbl_name, sql FROM sqlite_master WHERE type = 'trigger' AND name NOT LIKE 'sqlite_%';");
            if (result.Success)
            {
                int nameCol = GetColumnIndex(result, "name");
                int tblCol = GetColumnIndex(result, "tbl_name");
                int sqlCol = GetColumnIndex(result, "sql");

                foreach (var row in result.Rows)
                {
                    string name = GetString(row, nameCol);
                    string tblName = GetString(row, tblCol);
                    string sql = GetString(row, sqlCol);

                    if (!string.IsNullOrEmpty(name))
                    {
                        string timing = "AFTER";
                        var timingMatch = TriggerTimingRegex.Match(sql);
                        if (timingMatch.Success)
                        {
                            timing = Regex.Replace(timingMatch.Value.ToUpperInvariant(), @"\s+", " ");
                        }

                        string evt = "INSERT";
                        var evtMatch = TriggerEventRegex.Match(sql);
                        if (evtMatch.Success)
                        {
                            evt = evtMatch.Value.ToUpperInvariant();
                        }

                        list.Add(new TriggerSnapshot
                        {
                            Name = name,
                            Schema = "main",
                            TargetTableName = tblName,
                            Timing = timing,
                            Event = evt,
                            DefinitionDdl = sql
                        });
                    }
                }
            }
        }
        else if (db.Config.Type == DatabaseType.PostgreSQL)
        {
            string query = @"
                SELECT n.nspname AS schema_name,
                       t.tgname AS trigger_name,
                       c.relname AS table_name,
                       pg_get_triggerdef(t.oid, true) AS ddl
                FROM pg_trigger t
                JOIN pg_class c ON t.tgrelid = c.oid
                JOIN pg_namespace n ON c.relnamespace = n.oid
                WHERE NOT t.tgisinternal
                  AND n.nspname NOT IN ('pg_catalog', 'information_schema');";

            var result = await db.ExecuteQueryAsync(query);
            if (result.Success)
            {
                int schemaCol = GetColumnIndex(result, "schema_name");
                int nameCol = GetColumnIndex(result, "trigger_name");
                int tblCol = GetColumnIndex(result, "table_name");
                int ddlCol = GetColumnIndex(result, "ddl");

                foreach (var row in result.Rows)
                {
                    string schema = GetString(row, schemaCol);
                    string name = GetString(row, nameCol);
                    string tblName = GetString(row, tblCol);
                    string ddl = GetString(row, ddlCol);

                    if (!string.IsNullOrEmpty(name))
                    {
                        string timing = "AFTER";
                        var timingMatch = TriggerTimingRegex.Match(ddl);
                        if (timingMatch.Success)
                        {
                            timing = Regex.Replace(timingMatch.Value.ToUpperInvariant(), @"\s+", " ");
                        }

                        string evt = "INSERT";
                        var evtMatch = TriggerEventRegex.Match(ddl);
                        if (evtMatch.Success)
                        {
                            evt = evtMatch.Value.ToUpperInvariant();
                        }

                        list.Add(new TriggerSnapshot
                        {
                            Name = name,
                            Schema = schema,
                            TargetTableName = tblName,
                            Timing = timing,
                            Event = evt,
                            DefinitionDdl = ddl
                        });
                    }
                }
            }
        }

        return list;
    }

    private static int GetColumnIndex(QueryResult qr, string columnName)
    {
        for (int i = 0; i < qr.Columns.Count; i++)
        {
            if (string.Equals(qr.Columns[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string GetString(List<object?> row, int colIndex)
    {
        if (colIndex >= 0 && colIndex < row.Count && row[colIndex] != null)
        {
            return row[colIndex]!.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    public string SerializeSnapshot(SchemaSnapshotModel snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public SchemaSnapshotModel DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SchemaSnapshotModel();
        }

        return JsonSerializer.Deserialize<SchemaSnapshotModel>(json, JsonOptions) ?? new SchemaSnapshotModel();
    }

    public SchemaDiffResult CompareSnapshots(SchemaSnapshotModel source, SchemaSnapshotModel target, bool invertDirection = false)
    {
        return SchemaDiffEngine.Compare(source, target, invertDirection);
    }

    public SchemaDiffResult CompareSnapshots(SchemaSnapshotModel source, SchemaSnapshotModel target, SchemaCompareOptions options)
    {
        return SchemaDiffEngine.Compare(source, target, options);
    }

    public string GenerateSyncDdl(SchemaDiffResult diffResult, SyncOptions options)
    {
        return SyncDdlGenerator.Generate(diffResult, options);
    }

    public async Task<QueryResult> ApplySyncDdlAsync(IDatabaseService targetDb, string ddlScript)
    {
        if (string.IsNullOrWhiteSpace(ddlScript))
        {
            return new QueryResult { Success = true, AffectedRows = 0 };
        }

        // For SQLite, multiple DDL statements in a single command are supported by Microsoft.Data.Sqlite
        return await targetDb.ExecuteQueryAsync(ddlScript);
    }
}
