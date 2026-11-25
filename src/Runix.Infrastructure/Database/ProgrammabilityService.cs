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
        var results = _databaseService.Config.Type switch
        {
            DatabaseType.PostgreSQL => await GetPostgresObjectsAsync(),
            DatabaseType.Oracle => await GetOracleObjectsAsync(),
            DatabaseType.MySQL or DatabaseType.MariaDB => await GetMySqlObjectsAsync(),
            DatabaseType.SqlServer => await GetSqlServerObjectsAsync(),
            _ => await GetSqliteObjectsAsync()
        };

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

        if (_databaseService.Config.Type == DatabaseType.Oracle)
        {
            var oracleDdl = await GetOracleObjectDdlAsync(obj, cancellationToken);
            if (!string.IsNullOrWhiteSpace(oracleDdl))
            {
                return oracleDdl;
            }
        }
        else if (_databaseService.Config.Type == DatabaseType.PostgreSQL)
        {
            var pgDdl = await GetPostgresObjectDdlAsync(obj, cancellationToken);
            if (!string.IsNullOrWhiteSpace(pgDdl))
            {
                return pgDdl;
            }
        }
        else if (_databaseService.Config.Type is DatabaseType.MySQL or DatabaseType.MariaDB)
        {
            var mySqlDdl = await GetMySqlObjectDdlAsync(obj, cancellationToken);
            if (!string.IsNullOrWhiteSpace(mySqlDdl))
            {
                return mySqlDdl;
            }
        }
        else if (_databaseService.Config.Type == DatabaseType.SqlServer)
        {
            var ssDdl = await GetSqlServerObjectDdlAsync(obj, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ssDdl))
            {
                return ssDdl;
            }
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
        else if (_databaseService.Config.Type == DatabaseType.Oracle)
        {
            // Em Oracle via ADO.NET, scripts PL/SQL não devem conter o terminador de script '/'
            var cleanDdl = Regex.Replace(trimmedDdl, @"\s*/\s*$", "").Trim();
            commands.Add(new ParameterizedCommand(cleanDdl));
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

        // 1. Views (Lightweight: name + schema only for fast boot)
        const string viewsSql = @"
            SELECT schemaname, viewname
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
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.View,
                    SpecificId = $"view:{schema}.{name}"
                });
            }
        }

        // 2. Triggers (Lightweight metadata without decompiling triggerdef during boot)
        const string triggersSql = @"
            SELECT
                n.nspname AS schema_name,
                t.tgname AS trigger_name,
                c.relname AS table_name
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
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.Trigger,
                    TargetTableName = tableName,
                    SpecificId = $"trigger:{schema}.{tableName}.{name}"
                });
            }
        }

        // 3. Functions & Procedures (Lightweight metadata without full body decompilation)
        const string routinesSql = @"
            SELECT
                n.nspname AS schema_name,
                p.proname AS routine_name,
                p.oid::text AS oid_str,
                CASE WHEN p.prokind = 'p' THEN 'PROCEDURE' ELSE 'FUNCTION' END AS kind,
                pg_get_function_result(p.oid) AS return_type,
                pg_get_function_identity_arguments(p.oid) AS args,
                l.lanname AS lang
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
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = kind == "PROCEDURE" ? DatabaseObjectType.Procedure : DatabaseObjectType.Function,
                    ReturnType = returnType,
                    Arguments = args,
                    Language = lang,
                    SpecificId = $"routine:{oidStr}"
                });
            }
        }

        // 4. Indexes (Lightweight metadata)
        const string indexesSql = @"
            SELECT schemaname, indexname, tablename
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
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = DatabaseObjectType.Index,
                    TargetTableName = tableName,
                    SpecificId = $"index:{schema}.{name}"
                });
            }
        }

        return objects;
    }

    private async Task<string> GetPostgresObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        var schema = string.IsNullOrWhiteSpace(obj.Schema) ? "public" : obj.Schema.Trim();
        var name = obj.Name.Trim();

        try
        {
            switch (obj.ObjectType)
            {
                case DatabaseObjectType.Function or DatabaseObjectType.Procedure:
                {
                    string oidClause = "";
                    if (!string.IsNullOrEmpty(obj.SpecificId) && obj.SpecificId.StartsWith("routine:", StringComparison.OrdinalIgnoreCase))
                    {
                        var oidVal = obj.SpecificId.Substring("routine:".Length);
                        if (long.TryParse(oidVal, out _))
                        {
                            oidClause = $"p.oid = {oidVal}::oid";
                        }
                    }

                    if (string.IsNullOrEmpty(oidClause))
                    {
                        oidClause = $"n.nspname = '{schema.Replace("'", "''")}' AND p.proname = '{name.Replace("'", "''")}'";
                    }

                    var sql = $@"
                        SELECT pg_get_functiondef(p.oid) AS ddl
                        FROM pg_proc p
                        JOIN pg_namespace n ON p.pronamespace = n.oid
                        WHERE {oidClause}
                        LIMIT 1;";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 1, cancellationToken);
                    if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
                    {
                        return EnsureSemicolon(res.Rows[0][0]!.ToString()!);
                    }
                    break;
                }
                case DatabaseObjectType.Trigger:
                {
                    var sql = $@"
                        SELECT pg_get_triggerdef(t.oid, true) AS ddl
                        FROM pg_trigger t
                        JOIN pg_class c ON t.tgrelid = c.oid
                        JOIN pg_namespace n ON c.relnamespace = n.oid
                        WHERE n.nspname = '{schema.Replace("'", "''")}' 
                          AND t.tgname = '{name.Replace("'", "''")}'
                        LIMIT 1;";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 1, cancellationToken);
                    if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
                    {
                        return EnsureSemicolon(res.Rows[0][0]!.ToString()!);
                    }
                    break;
                }
                case DatabaseObjectType.View:
                {
                    var sql = $@"
                        SELECT definition
                        FROM pg_views
                        WHERE schemaname = '{schema.Replace("'", "''")}' 
                          AND viewname = '{name.Replace("'", "''")}'
                        LIMIT 1;";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 1, cancellationToken);
                    if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
                    {
                        var def = res.Rows[0][0]!.ToString()!.Trim();
                        return $"CREATE OR REPLACE VIEW \"{schema}\".\"{name}\" AS\n{EnsureSemicolon(def)}";
                    }
                    break;
                }
                case DatabaseObjectType.Index:
                {
                    var sql = $@"
                        SELECT indexdef
                        FROM pg_indexes
                        WHERE schemaname = '{schema.Replace("'", "''")}' 
                          AND indexname = '{name.Replace("'", "''")}'
                        LIMIT 1;";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 1, cancellationToken);
                    if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
                    {
                        return EnsureSemicolon(res.Rows[0][0]!.ToString()!);
                    }
                    break;
                }
            }
        }
        catch
        {
            // Ignore and fall back to template
        }

        return GenerateTemplateDdl(obj.ObjectType, obj.Name, obj.TargetTableName);
    }

    private async Task<List<DatabaseObjectInfo>> GetMySqlObjectsAsync()
    {
        var objects = new List<DatabaseObjectInfo>();
        var dbName = !string.IsNullOrWhiteSpace(_databaseService.Config.Database)
            ? _databaseService.Config.Database
            : "mysql";

        // 1. Routines (Procedures & Functions)
        var routineSql = $@"
            SELECT ROUTINE_NAME, ROUTINE_TYPE, DTD_IDENTIFIER
            FROM information_schema.routines
            WHERE ROUTINE_SCHEMA = '{dbName.Replace("'", "''")}'
            ORDER BY ROUTINE_TYPE, ROUTINE_NAME;";

        var routRes = await _databaseService.ExecuteQueryAsync(routineSql, 5000);
        if (routRes.Success)
        {
            foreach (var row in routRes.Rows)
            {
                var name = GetString(routRes, row, "ROUTINE_NAME");
                var type = GetString(routRes, row, "ROUTINE_TYPE").ToUpperInvariant();
                var ret = GetString(routRes, row, "DTD_IDENTIFIER");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var isProc = type == "PROCEDURE";
                objects.Add(new DatabaseObjectInfo
                {
                    Schema = dbName,
                    Name = name,
                    ObjectType = isProc ? DatabaseObjectType.Procedure : DatabaseObjectType.Function,
                    ReturnType = isProc ? null : ret,
                    SpecificId = $"mysql_routine:{dbName}.{name}"
                });
            }
        }

        // 2. Triggers
        var triggerSql = $@"
            SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE
            FROM information_schema.triggers
            WHERE TRIGGER_SCHEMA = '{dbName.Replace("'", "''")}'
            ORDER BY TRIGGER_NAME;";

        var trigRes = await _databaseService.ExecuteQueryAsync(triggerSql, 5000);
        if (trigRes.Success)
        {
            foreach (var row in trigRes.Rows)
            {
                var name = GetString(trigRes, row, "TRIGGER_NAME");
                var tbl = GetString(trigRes, row, "EVENT_OBJECT_TABLE");
                if (string.IsNullOrWhiteSpace(name)) continue;

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = dbName,
                    Name = name,
                    ObjectType = DatabaseObjectType.Trigger,
                    TargetTableName = tbl,
                    SpecificId = $"mysql_trigger:{dbName}.{name}"
                });
            }
        }

        return objects;
    }

    private async Task<string> GetMySqlObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        try
        {
            string showCmd = obj.ObjectType switch
            {
                DatabaseObjectType.Procedure => $"SHOW CREATE PROCEDURE `{obj.Name.Replace("`", "``")}`;",
                DatabaseObjectType.Function => $"SHOW CREATE FUNCTION `{obj.Name.Replace("`", "``")}`;",
                DatabaseObjectType.Trigger => $"SHOW CREATE TRIGGER `{obj.Name.Replace("`", "``")}`;",
                _ => ""
            };

            if (!string.IsNullOrEmpty(showCmd))
            {
                var res = await _databaseService.ExecuteQueryAsync(showCmd, 1, cancellationToken);
                if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count >= 3 && res.Rows[0][2] != null)
                {
                    return EnsureSemicolon(res.Rows[0][2]!.ToString()!);
                }
            }
        }
        catch
        {
            // Ignore
        }

        return GenerateTemplateDdl(obj.ObjectType, obj.Name, obj.TargetTableName);
    }

    private async Task<List<DatabaseObjectInfo>> GetSqlServerObjectsAsync()
    {
        var objects = new List<DatabaseObjectInfo>();

        const string sql = @"
            SELECT 
                s.name AS SchemaName,
                o.name AS ObjectName,
                o.type AS ObjectType,
                OBJECT_NAME(o.parent_object_id) AS TargetTable
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.is_ms_shipped = 0 
              AND o.type IN ('P', 'FN', 'IF', 'TF', 'TR')
            ORDER BY o.type, s.name, o.name;";

        var res = await _databaseService.ExecuteQueryAsync(sql, 5000);
        if (res.Success)
        {
            foreach (var row in res.Rows)
            {
                var schema = GetString(res, row, "SchemaName", "dbo");
                var name = GetString(res, row, "ObjectName");
                var type = GetString(res, row, "ObjectType").Trim().ToUpperInvariant();
                var targetTbl = GetString(res, row, "TargetTable");
                if (string.IsNullOrWhiteSpace(name)) continue;

                var objType = type switch
                {
                    "P" => DatabaseObjectType.Procedure,
                    "FN" or "IF" or "TF" => DatabaseObjectType.Function,
                    "TR" => DatabaseObjectType.Trigger,
                    _ => DatabaseObjectType.Procedure
                };

                objects.Add(new DatabaseObjectInfo
                {
                    Schema = schema,
                    Name = name,
                    ObjectType = objType,
                    TargetTableName = string.IsNullOrWhiteSpace(targetTbl) ? null : targetTbl,
                    SpecificId = $"sqlserver:{schema}.{name}"
                });
            }
        }

        return objects;
    }

    private async Task<string> GetSqlServerObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        try
        {
            var schema = string.IsNullOrWhiteSpace(obj.Schema) ? "dbo" : obj.Schema;
            var sql = $"SELECT OBJECT_DEFINITION(OBJECT_ID(N'[{schema}].[{obj.Name}]'));";
            var res = await _databaseService.ExecuteQueryAsync(sql, 1, cancellationToken);
            if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0 && res.Rows[0][0] != null)
            {
                return EnsureSemicolon(res.Rows[0][0]!.ToString()!);
            }
        }
        catch
        {
            // Ignore
        }

        return GenerateTemplateDdl(obj.ObjectType, obj.Name, obj.TargetTableName);
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

    private async Task<List<DatabaseObjectInfo>> GetOracleObjectsAsync()
    {
        var objects = new List<DatabaseObjectInfo>();
        var currentUser = (!string.IsNullOrWhiteSpace(_databaseService.Config.Username) ? _databaseService.Config.Username : "").Trim().ToUpperInvariant();
        var ownerCondition = !string.IsNullOrEmpty(currentUser)
            ? $"(OWNER = '{currentUser}' OR OWNER = USER)"
            : "OWNER = USER";
        var seqOwnerCondition = !string.IsNullOrEmpty(currentUser)
            ? $"(SEQUENCE_OWNER = '{currentUser}' OR SEQUENCE_OWNER = USER)"
            : "SEQUENCE_OWNER = USER";

        // 1. Views
        try
        {
            var viewsSql = $@"
                SELECT OWNER, VIEW_NAME
                FROM ALL_VIEWS
                WHERE {ownerCondition}
                ORDER BY OWNER, VIEW_NAME";

            var viewsRes = await _databaseService.ExecuteQueryAsync(viewsSql, 5000);
            if (!viewsRes.Success || viewsRes.Rows.Count == 0)
            {
                viewsRes = await _databaseService.ExecuteQueryAsync("SELECT USER AS OWNER, VIEW_NAME FROM USER_VIEWS ORDER BY VIEW_NAME", 5000);
            }

            if (viewsRes.Success)
            {
                foreach (var row in viewsRes.Rows)
                {
                    var owner = GetString(viewsRes, row, "OWNER", currentUser);
                    var name = GetString(viewsRes, row, "VIEW_NAME");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    objects.Add(new DatabaseObjectInfo
                    {
                        Schema = owner,
                        Name = name,
                        ObjectType = DatabaseObjectType.View,
                        SpecificId = $"view:{owner}.{name}"
                    });
                }
            }
        }
        catch { /* Fallback silencioso */ }

        // 2. Triggers
        try
        {
            var triggersSql = $@"
                SELECT OWNER, TRIGGER_NAME, TABLE_OWNER, TABLE_NAME, STATUS
                FROM ALL_TRIGGERS
                WHERE {ownerCondition}
                ORDER BY OWNER, TRIGGER_NAME";

            var triggersRes = await _databaseService.ExecuteQueryAsync(triggersSql, 5000);
            if (!triggersRes.Success || triggersRes.Rows.Count == 0)
            {
                triggersRes = await _databaseService.ExecuteQueryAsync("SELECT USER AS OWNER, TRIGGER_NAME, TABLE_OWNER, TABLE_NAME, STATUS FROM USER_TRIGGERS ORDER BY TRIGGER_NAME", 5000);
            }

            if (triggersRes.Success)
            {
                foreach (var row in triggersRes.Rows)
                {
                    var owner = GetString(triggersRes, row, "OWNER", currentUser);
                    var name = GetString(triggersRes, row, "TRIGGER_NAME");
                    var tblName = GetString(triggersRes, row, "TABLE_NAME");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    objects.Add(new DatabaseObjectInfo
                    {
                        Schema = owner,
                        Name = name,
                        ObjectType = DatabaseObjectType.Trigger,
                        TargetTableName = tblName,
                        SpecificId = $"trigger:{owner}.{name}"
                    });
                }
            }
        }
        catch { /* Fallback silencioso */ }

        // 3. Procedures, Functions, Packages
        try
        {
            var objectsSql = $@"
                SELECT OWNER, OBJECT_NAME, OBJECT_TYPE, STATUS
                FROM ALL_OBJECTS
                WHERE OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION', 'PACKAGE')
                  AND {ownerCondition}
                ORDER BY OBJECT_TYPE, OWNER, OBJECT_NAME";

            var objectsRes = await _databaseService.ExecuteQueryAsync(objectsSql, 5000);
            if (!objectsRes.Success || objectsRes.Rows.Count == 0)
            {
                objectsRes = await _databaseService.ExecuteQueryAsync("SELECT USER AS OWNER, OBJECT_NAME, OBJECT_TYPE, STATUS FROM USER_OBJECTS WHERE OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION', 'PACKAGE') ORDER BY OBJECT_TYPE, OBJECT_NAME", 5000);
            }

            if (objectsRes.Success)
            {
                var packageSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in objectsRes.Rows)
                {
                    var owner = GetString(objectsRes, row, "OWNER", currentUser);
                    var name = GetString(objectsRes, row, "OBJECT_NAME");
                    var type = GetString(objectsRes, row, "OBJECT_TYPE").ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var objType = type switch
                    {
                        "PACKAGE" => DatabaseObjectType.Package,
                        "PROCEDURE" => DatabaseObjectType.Procedure,
                        "FUNCTION" => DatabaseObjectType.Function,
                        _ => DatabaseObjectType.Procedure
                    };

                    if (objType == DatabaseObjectType.Package)
                    {
                        var key = $"{owner}.{name}";
                        if (packageSeen.Contains(key)) continue;
                        packageSeen.Add(key);
                    }

                    objects.Add(new DatabaseObjectInfo
                    {
                        Schema = owner,
                        Name = name,
                        ObjectType = objType,
                        SpecificId = $"{objType.ToString().ToLowerInvariant()}:{owner}.{name}"
                    });
                }
            }
        }
        catch { /* Fallback silencioso */ }

        // 4. Sequences
        try
        {
            var seqSql = $@"
                SELECT SEQUENCE_OWNER, SEQUENCE_NAME, MIN_VALUE, MAX_VALUE, INCREMENT_BY, LAST_NUMBER
                FROM ALL_SEQUENCES
                WHERE {seqOwnerCondition}
                ORDER BY SEQUENCE_OWNER, SEQUENCE_NAME";

            var seqRes = await _databaseService.ExecuteQueryAsync(seqSql, 5000);
            if (!seqRes.Success || seqRes.Rows.Count == 0)
            {
                seqRes = await _databaseService.ExecuteQueryAsync("SELECT USER AS SEQUENCE_OWNER, SEQUENCE_NAME, MIN_VALUE, MAX_VALUE, INCREMENT_BY, LAST_NUMBER FROM USER_SEQUENCES ORDER BY SEQUENCE_NAME", 5000);
            }

            if (seqRes.Success)
            {
                foreach (var row in seqRes.Rows)
                {
                    var owner = GetString(seqRes, row, "SEQUENCE_OWNER", currentUser);
                    var name = GetString(seqRes, row, "SEQUENCE_NAME");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    objects.Add(new DatabaseObjectInfo
                    {
                        Schema = owner,
                        Name = name,
                        ObjectType = DatabaseObjectType.Sequence,
                        SpecificId = $"sequence:{owner}.{name}"
                    });
                }
            }
        }
        catch { /* Fallback silencioso */ }

        return objects;
    }

    private async Task<string> GetOracleObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default)
    {
        var rawName = obj.Name.Trim();
        var (schema, name) = SplitOracleSchemaAndName(obj.Schema, rawName);

        try
        {
            switch (obj.ObjectType)
            {
                case DatabaseObjectType.Package:
                {
                    // Specification e Body unificados a partir de ALL_SOURCE separados por /
                    var sql = $@"
                        SELECT TYPE, LINE, TEXT
                        FROM ALL_SOURCE
                        WHERE OWNER = '{schema}' AND NAME = '{name}' AND TYPE IN ('PACKAGE', 'PACKAGE BODY')
                        ORDER BY CASE WHEN TYPE = 'PACKAGE' THEN 1 ELSE 2 END, LINE";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 50000, cancellationToken);
                    if (!res.Success || res.Rows.Count == 0)
                    {
                        var userSql = $@"
                            SELECT TYPE, LINE, TEXT
                            FROM USER_SOURCE
                            WHERE NAME = '{name}' AND TYPE IN ('PACKAGE', 'PACKAGE BODY')
                            ORDER BY CASE WHEN TYPE = 'PACKAGE' THEN 1 ELSE 2 END, LINE";
                        res = await _databaseService.ExecuteQueryAsync(userSql, 50000, cancellationToken);
                    }

                    if (res.Success && res.Rows.Count > 0)
                    {
                        var specLines = new List<string>();
                        var bodyLines = new List<string>();

                        foreach (var row in res.Rows)
                        {
                            var type = GetString(res, row, "TYPE").ToUpperInvariant();
                            var text = GetString(res, row, "TEXT");
                            if (type == "PACKAGE")
                            {
                                specLines.Add(text);
                            }
                            else if (type == "PACKAGE BODY")
                            {
                                bodyLines.Add(text);
                            }
                        }

                        var specText = string.Join("", specLines).Trim();
                        var bodyText = string.Join("", bodyLines).Trim();

                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(specText))
                        {
                            parts.Add(specText.EndsWith('/') ? specText : specText + "\n/");
                        }
                        if (!string.IsNullOrWhiteSpace(bodyText))
                        {
                            parts.Add(bodyText.EndsWith('/') ? bodyText : bodyText + "\n/");
                        }

                        if (parts.Count > 0)
                        {
                            return string.Join("\n\n", parts);
                        }
                    }
                    break;
                }

                case DatabaseObjectType.Sequence:
                {
                    var sql = $@"
                        SELECT SEQUENCE_OWNER, SEQUENCE_NAME, MIN_VALUE, MAX_VALUE, INCREMENT_BY, LAST_NUMBER, CACHE_SIZE, CYCLE_FLAG
                        FROM ALL_SEQUENCES
                        WHERE SEQUENCE_OWNER = '{schema}' AND SEQUENCE_NAME = '{name}'";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 10, cancellationToken);
                    if (!res.Success || res.Rows.Count == 0)
                    {
                        var userSql = $@"
                            SELECT '{schema}' AS SEQUENCE_OWNER, SEQUENCE_NAME, MIN_VALUE, MAX_VALUE, INCREMENT_BY, LAST_NUMBER, CACHE_SIZE, CYCLE_FLAG
                            FROM USER_SEQUENCES
                            WHERE SEQUENCE_NAME = '{name}'";
                        res = await _databaseService.ExecuteQueryAsync(userSql, 10, cancellationToken);
                    }

                    if (res.Success && res.Rows.Count > 0)
                    {
                        var row = res.Rows[0];
                        var minVal = GetString(res, row, "MIN_VALUE", "1");
                        var maxVal = GetString(res, row, "MAX_VALUE", "999999999999999999999999999");
                        var incBy = GetString(res, row, "INCREMENT_BY", "1");
                        var lastNum = GetString(res, row, "LAST_NUMBER", "1");
                        var cacheSizeStr = GetString(res, row, "CACHE_SIZE", "0");
                        var cacheSize = long.TryParse(cacheSizeStr, out var cs) ? cs : 0L;
                        var cycleFlag = GetString(res, row, "CYCLE_FLAG", "N");

                        return $"CREATE SEQUENCE \"{schema}\".\"{name}\"\nSTART WITH {lastNum}\nINCREMENT BY {incBy}\nMINVALUE {minVal}\nMAXVALUE {maxVal}\n{(cacheSize > 0 ? $"CACHE {cacheSize}" : "NOCACHE")}\n{(cycleFlag == "Y" ? "CYCLE" : "NOCYCLE")};";
                    }
                    break;
                }

                case DatabaseObjectType.Procedure or DatabaseObjectType.Function or DatabaseObjectType.Trigger:
                {
                    var typeStr = obj.ObjectType switch
                    {
                        DatabaseObjectType.Procedure => "PROCEDURE",
                        DatabaseObjectType.Function => "FUNCTION",
                        DatabaseObjectType.Trigger => "TRIGGER",
                        _ => "PROCEDURE"
                    };

                    var sql = $@"
                        SELECT LINE, TEXT
                        FROM ALL_SOURCE
                        WHERE OWNER = '{schema}' AND NAME = '{name}' AND TYPE = '{typeStr}'
                        ORDER BY LINE";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 50000, cancellationToken);
                    if (!res.Success || res.Rows.Count == 0)
                    {
                        var userSql = $@"
                            SELECT LINE, TEXT
                            FROM USER_SOURCE
                            WHERE NAME = '{name}' AND TYPE = '{typeStr}'
                            ORDER BY LINE";
                        res = await _databaseService.ExecuteQueryAsync(userSql, 50000, cancellationToken);
                    }

                    if (res.Success && res.Rows.Count > 0)
                    {
                        var lines = res.Rows.Select(r => GetString(res, r, "TEXT"));
                        var fullText = string.Join("", lines).Trim();
                        return fullText.EndsWith('/') ? fullText : fullText + "\n/";
                    }
                    break;
                }

                case DatabaseObjectType.View:
                {
                    var sql = $@"
                        SELECT TEXT
                        FROM ALL_VIEWS
                        WHERE OWNER = '{schema}' AND VIEW_NAME = '{name}'";

                    var res = await _databaseService.ExecuteQueryAsync(sql, 10, cancellationToken);
                    if (!res.Success || res.Rows.Count == 0)
                    {
                        var userSql = $@"
                            SELECT TEXT
                            FROM USER_VIEWS
                            WHERE VIEW_NAME = '{name}'";
                        res = await _databaseService.ExecuteQueryAsync(userSql, 10, cancellationToken);
                    }

                    if (res.Success && res.Rows.Count > 0)
                    {
                        var text = GetString(res, res.Rows[0], "TEXT").Trim();
                        return $"CREATE OR REPLACE VIEW \"{schema}\".\"{name}\" AS\n{EnsureSemicolon(text)}";
                    }
                    break;
                }
            }
        }
        catch
        {
            // Silencioso em caso de erro na extração
        }

        return GenerateTemplateDdl(obj.ObjectType, obj.Name, obj.TargetTableName);
    }

    private (string schema, string name) SplitOracleSchemaAndName(string? schema, string name)
    {
        var clean = name.Replace("\"", "").Trim();
        var defaultOwner = !string.IsNullOrWhiteSpace(_databaseService.Config.Username)
            ? _databaseService.Config.Username.Trim().ToUpperInvariant()
            : "USER";

        if (clean.Contains('.'))
        {
            var parts = clean.Split('.');
            return (parts[0].ToUpperInvariant(), parts[1].ToUpperInvariant());
        }

        var resolvedSchema = !string.IsNullOrWhiteSpace(schema) && !schema.Equals("main", StringComparison.OrdinalIgnoreCase) && !schema.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? schema.Trim().ToUpperInvariant()
            : defaultOwner;

        return (resolvedSchema, clean.ToUpperInvariant());
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

        if (dbType == DatabaseType.Oracle)
        {
            return obj.ObjectType switch
            {
                DatabaseObjectType.View => $"DROP VIEW {qualified};",
                DatabaseObjectType.Trigger => $"DROP TRIGGER {qualified};",
                DatabaseObjectType.Function => $"DROP FUNCTION {qualified};",
                DatabaseObjectType.Procedure => $"DROP PROCEDURE {qualified};",
                DatabaseObjectType.Package => $"DROP PACKAGE {qualified};",
                DatabaseObjectType.Sequence => $"DROP SEQUENCE {qualified};",
                DatabaseObjectType.Index => $"DROP INDEX {qualified};",
                _ => string.Empty
            };
        }

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
