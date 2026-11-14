using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Database;

public class ProgrammabilityService : IProgrammabilityService
{
    private readonly IDatabaseService _databaseService;

    public ProgrammabilityService(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    public async Task<List<DatabaseObjectInfo>> GetProgrammableObjectsAsync(
        DatabaseObjectType? filterType = null,
        CancellationToken cancellationToken = default)
    {
        var results = _databaseService.Config.Type == DatabaseType.PostgreSQL
            ? await GetPostgresObjectsAsync()
            : await GetSqliteObjectsAsync();

        if (filterType.HasValue)
        {
            results = results.Where(o => o.ObjectType == filterType.Value).ToList();
        }

        return results;
    }

    public async Task<string> GetObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (!string.IsNullOrWhiteSpace(obj.DefinitionDdl))
        {
            return EnsureSemicolon(obj.DefinitionDdl);
        }

        var allObjects = await GetProgrammableObjectsAsync(obj.ObjectType, cancellationToken);
        var match = allObjects.FirstOrDefault(o =>
            string.Equals(o.Name, obj.Name, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(obj.SpecificId) || string.Equals(o.SpecificId, obj.SpecificId, StringComparison.OrdinalIgnoreCase)));

        return match != null && !string.IsNullOrWhiteSpace(match.DefinitionDdl)
            ? EnsureSemicolon(match.DefinitionDdl)
            : GenerateTemplateDdl(obj.ObjectType, obj.Name, obj.TargetTableName);
    }

    public async Task<QueryResult> ApplyObjectDdlAsync(
        DatabaseObjectInfo obj,
        string newDdl,
        bool isExistingObject = true,
        CancellationToken cancellationToken = default)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (string.IsNullOrWhiteSpace(newDdl))
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = "A definição DDL não pode estar vazia."
            };
        }

        var commands = new List<ParameterizedCommand>();
        var trimmedDdl = newDdl.Trim();

        if (_databaseService.Config.Type == DatabaseType.SQLite)
        {
            // Normaliza CREATE OR REPLACE VIEW/TRIGGER para SQLite
            trimmedDdl = Regex.Replace(
                trimmedDdl,
                @"^\s*CREATE\s+OR\s+REPLACE\s+(VIEW|TRIGGER)\b",
                "CREATE $1",
                RegexOptions.IgnoreCase);

            if (isExistingObject && !string.IsNullOrWhiteSpace(obj.Name))
            {
                var dropSql = BuildDropStatement(obj, DatabaseType.SQLite, ifExists: true);
                if (!string.IsNullOrWhiteSpace(dropSql))
                {
                    commands.Add(new ParameterizedCommand(dropSql));
                }
            }

            commands.Add(new ParameterizedCommand(trimmedDdl));
        }
        else
        {
            // PostgreSQL: Para Índices (e Triggers sem OR REPLACE), executa DROP IF EXISTS + CREATE na mesma transação
            if (isExistingObject && !string.IsNullOrWhiteSpace(obj.Name) && obj.ObjectType == DatabaseObjectType.Index)
            {
                var dropSql = BuildDropStatement(obj, DatabaseType.PostgreSQL, ifExists: true);
                if (!string.IsNullOrWhiteSpace(dropSql))
                {
                    commands.Add(new ParameterizedCommand(dropSql));
                }
            }

            commands.Add(new ParameterizedCommand(trimmedDdl));
        }

        return await _databaseService.ExecuteBatchTransactionalAsync(commands, cancellationToken);
    }

    public async Task<QueryResult> DropObjectAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        var dropSql = BuildDropStatement(obj, _databaseService.Config.Type, ifExists: true);
        if (string.IsNullOrWhiteSpace(dropSql))
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = $"Operação DROP não suportada para {obj.ObjectType}."
            };
        }

        return await _databaseService.ExecuteBatchTransactionalAsync(
            new[] { new ParameterizedCommand(dropSql) },
            cancellationToken);
    }

    public string GenerateTemplateDdl(DatabaseObjectType objectType, string objectName = "new_object", string? targetTable = null)
    {
        return DatabaseObjectTemplateGenerator.GenerateTemplate(
            objectType,
            _databaseService.Config.Type,
            objectName,
            targetTable);
    }

    private async Task<List<DatabaseObjectInfo>> GetSqliteObjectsAsync()
    {
        var objects = new List<DatabaseObjectInfo>();
        const string sql = @"
            SELECT type, name, tbl_name, sql
            FROM sqlite_master
            WHERE type IN ('view', 'trigger', 'index')
              AND name NOT LIKE 'sqlite_%'
              AND sql IS NOT NULL
            ORDER BY type, name;";

        var res = await _databaseService.ExecuteQueryAsync(sql, 5000);
        if (!res.Success) return objects;

        foreach (var row in res.Rows)
        {
            var typeStr = GetString(res, row, "type").ToLowerInvariant();
            var name = GetString(res, row, "name");
            var tblName = GetString(res, row, "tbl_name");
            var ddl = GetString(res, row, "sql");

            if (string.IsNullOrWhiteSpace(name)) continue;

            var objType = typeStr switch
            {
                "view" => DatabaseObjectType.View,
                "trigger" => DatabaseObjectType.Trigger,
                "index" => DatabaseObjectType.Index,
                _ => DatabaseObjectType.View
            };

            objects.Add(new DatabaseObjectInfo
            {
                Schema = "main",
                Name = name,
                ObjectType = objType,
                TargetTableName = objType == DatabaseObjectType.View ? null : tblName,
                IsUnique = objType == DatabaseObjectType.Index && ddl.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase),
                DefinitionDdl = EnsureSemicolon(ddl),
                SpecificId = $"{typeStr}:{name}"
            });
        }

        return objects;
    }

    private async Task<List<DatabaseObjectInfo>> GetPostgresObjectsAsync()
    {
        var objects = new List<DatabaseObjectInfo>();

        // 1. Views
        const string viewsSql = @"
            SELECT schemaname, viewname, definition
            FROM pg_views
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY schemaname, viewname;";

        var viewsRes = await _databaseService.ExecuteQueryAsync(viewsSql, 5000);
        if (viewsRes.Success)
        {
            foreach (var row in viewsRes.Rows)
            {
                var schema = GetString(viewsRes, row, "schemaname", "public");
                var name = GetString(viewsRes, row, "viewname");
                var def = GetString(viewsRes, row, "definition").Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var fullDdl = $"CREATE OR REPLACE VIEW \"{schema}\".\"{name}\" AS\n{EnsureSemicolon(def)}";
                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.View,
                    DefinitionDdl = fullDdl,
                    SpecificId = $"view:{schema}.{name}"
                });
            }
        }

        // 2. Triggers
        const string triggersSql = @"
            SELECT
                n.nspname AS schema_name,
                t.tgname AS trigger_name,
                c.relname AS table_name,
                pg_get_triggerdef(t.oid, true) AS ddl
            FROM pg_trigger t
            JOIN pg_class c ON t.tgrelid = c.oid
            JOIN pg_namespace n ON c.relnamespace = n.oid
            WHERE NOT t.tgisinternal
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY n.nspname, t.tgname;";

        var triggersRes = await _databaseService.ExecuteQueryAsync(triggersSql, 5000);
        if (triggersRes.Success)
        {
            foreach (var row in triggersRes.Rows)
            {
                var schema = GetString(triggersRes, row, "schema_name", "public");
                var name = GetString(triggersRes, row, "trigger_name");
                var tableName = GetString(triggersRes, row, "table_name");
                var ddl = GetString(triggersRes, row, "ddl");
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.Trigger,
                    TargetTableName = tableName,
                    DefinitionDdl = EnsureSemicolon(ddl),
                    SpecificId = $"trigger:{schema}.{tableName}.{name}"
                });
            }
        }

        // 3. Functions & Procedures
        const string routinesSql = @"
            SELECT
                n.nspname AS schema_name,
                p.proname AS routine_name,
                p.oid::text AS oid_str,
                CASE WHEN p.prokind = 'p' THEN 'PROCEDURE' ELSE 'FUNCTION' END AS kind,
                pg_get_function_result(p.oid) AS return_type,
                pg_get_function_identity_arguments(p.oid) AS args,
                l.lanname AS lang,
                pg_get_functiondef(p.oid) AS ddl
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            JOIN pg_language l ON p.prolang = l.oid
            WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND p.prokind IN ('f', 'p')
            ORDER BY n.nspname, p.proname;";

        var routinesRes = await _databaseService.ExecuteQueryAsync(routinesSql, 5000);
        if (routinesRes.Success)
        {
            foreach (var row in routinesRes.Rows)
            {
                var schema = GetString(routinesRes, row, "schema_name", "public");
                var name = GetString(routinesRes, row, "routine_name");
                var oidStr = GetString(routinesRes, row, "oid_str");
                var kind = GetString(routinesRes, row, "kind", "FUNCTION");
                var returnType = GetString(routinesRes, row, "return_type");
                var args = GetString(routinesRes, row, "args");
                var lang = GetString(routinesRes, row, "lang", "sql");
                var ddl = GetString(routinesRes, row, "ddl");
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = kind == "PROCEDURE" ? DatabaseObjectType.Procedure : DatabaseObjectType.Function,
                    ReturnType = returnType,
                    Arguments = args,
                    Language = lang,
                    DefinitionDdl = EnsureSemicolon(ddl),
                    SpecificId = $"routine:{oidStr}"
                });
            }
        }

        // 4. Indexes
        const string indexesSql = @"
            SELECT schemaname, indexname, tablename, indexdef
            FROM pg_indexes
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            ORDER BY schemaname, indexname;";

        var indexesRes = await _databaseService.ExecuteQueryAsync(indexesSql, 5000);
        if (indexesRes.Success)
        {
            foreach (var row in indexesRes.Rows)
            {
                var schema = GetString(indexesRes, row, "schemaname", "public");
                var name = GetString(indexesRes, row, "indexname");
                var tableName = GetString(indexesRes, row, "tablename");
                var ddl = GetString(indexesRes, row, "indexdef");
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.Index,
                    TargetTableName = tableName,
                    IsUnique = ddl.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase),
                    DefinitionDdl = EnsureSemicolon(ddl),
                    SpecificId = $"index:{schema}.{name}"
                });
            }
        }

        return objects;
    }

    private static string GetString(QueryResult res, List<object?> row, string key, string fallback = "")
    {
        var idx = res.Columns.FindIndex(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx < row.Count && row[idx] != null)
        {
            return row[idx]?.ToString() ?? fallback;
        }
        return fallback;
    }

    private static string BuildDropStatement(DatabaseObjectInfo obj, DatabaseType dbType, bool ifExists)
    {
        var existsClause = ifExists ? "IF EXISTS " : string.Empty;
        var quotedName = EscapeIdent(obj.Name);

        if (dbType == DatabaseType.SQLite)
        {
            return obj.ObjectType switch
            {
                DatabaseObjectType.View => $"DROP VIEW {existsClause}{quotedName};",
                DatabaseObjectType.Trigger => $"DROP TRIGGER {existsClause}{quotedName};",
                DatabaseObjectType.Index => $"DROP INDEX {existsClause}{quotedName};",
                _ => string.Empty
            };
        }

        var qualified = string.IsNullOrWhiteSpace(obj.Schema)
            ? quotedName
            : $"{EscapeIdent(obj.Schema)}.{quotedName}";

        return obj.ObjectType switch
        {
            DatabaseObjectType.View => $"DROP VIEW {existsClause}{qualified};",
            DatabaseObjectType.Trigger when !string.IsNullOrWhiteSpace(obj.TargetTableName) =>
                $"DROP TRIGGER {existsClause}{quotedName} ON {(string.IsNullOrWhiteSpace(obj.Schema) ? EscapeIdent(obj.TargetTableName) : $"{EscapeIdent(obj.Schema)}.{EscapeIdent(obj.TargetTableName)}")};",
            DatabaseObjectType.Function => $"DROP FUNCTION {existsClause}{qualified}({obj.Arguments});",
            DatabaseObjectType.Procedure => $"DROP PROCEDURE {existsClause}{qualified}({obj.Arguments});",
            DatabaseObjectType.Index => $"DROP INDEX {existsClause}{qualified};",
            _ => string.Empty
        };
    }

    private static string EscapeIdent(string ident)
    {
        return $"\"{ident.Replace("\"", "\"\"")}\"";
    }

    private static string EnsureSemicolon(string sql)
    {
        var trimmed = sql.Trim();
        if (string.IsNullOrEmpty(trimmed)) return string.Empty;
        return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
    }
}
