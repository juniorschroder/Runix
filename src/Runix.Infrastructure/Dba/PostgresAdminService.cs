using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.Dba;

namespace Runix.Infrastructure.Dba;

/// <summary>
/// Implementação dos serviços administrativos de PostgreSQL para gerenciamento de Roles,
/// Matriz de Privilégios e Sessões Ativas.
/// </summary>
public class PostgresAdminService : IPostgresAdminService
{
    public static PostgresAdminService Instance { get; } = new();

    public async Task<IReadOnlyList<PostgresRoleInfo>> GetRolesAsync(IDatabaseService db)
    {
        const string sql = @"
SELECT 
    rolname,
    rolsuper,
    rolinherit,
    rolcreaterole,
    rolcreatedb,
    rolcanlogin,
    rolreplication,
    rolconnlimit,
    rolvaliduntil::text AS rolvaliduntil
FROM pg_roles
ORDER BY rolsuper DESC, rolcanlogin DESC, rolname ASC;";

        var result = await db.ExecuteQueryAsync(sql);
        var list = new List<PostgresRoleInfo>();

        if (!result.Success || result.Rows.Count == 0)
        {
            return list;
        }

        foreach (var row in result.Rows)
        {
            if (row.Count < 9) continue;

            var role = new PostgresRoleInfo
            {
                RoleName = row[0]?.ToString() ?? string.Empty,
                IsSuperuser = ConvertToBool(row[1]),
                InheritsPrivileges = ConvertToBool(row[2]),
                CanCreateRole = ConvertToBool(row[3]),
                CanCreateDb = ConvertToBool(row[4]),
                CanLogin = ConvertToBool(row[5]),
                IsReplication = ConvertToBool(row[6]),
                ConnectionLimit = ConvertToInt(row[7], -1),
                ValidUntil = ParseValidUntil(row[8]?.ToString())
            };

            list.Add(role);
        }

        return list;
    }

    public string GenerateCreateRoleDdl(PostgresRoleInfo role, string? password)
    {
        var quotedRole = QuoteIdentifier(role.RoleName);
        var clauses = new List<string>
        {
            role.CanLogin ? "LOGIN" : "NOLOGIN",
            role.IsSuperuser ? "SUPERUSER" : "NOSUPERUSER",
            role.CanCreateDb ? "CREATEDB" : "NOCREATEDB",
            role.CanCreateRole ? "CREATEROLE" : "NOCREATEROLE",
            role.InheritsPrivileges ? "INHERIT" : "NOINHERIT",
            role.IsReplication ? "REPLICATION" : "NOREPLICATION"
        };

        if (role.ConnectionLimit >= 0)
        {
            clauses.Add($"CONNECTION LIMIT {role.ConnectionLimit}");
        }

        if (!string.IsNullOrEmpty(password))
        {
            clauses.Add($"PASSWORD '{EscapeLiteral(password)}'");
        }

        if (role.ValidUntil.HasValue)
        {
            clauses.Add($"VALID UNTIL '{role.ValidUntil.Value:yyyy-MM-dd HH:mm:ss}'");
        }

        return $"CREATE ROLE {quotedRole} WITH {string.Join(" ", clauses)};";
    }

    public string GenerateAlterRoleDdl(PostgresRoleInfo role, string? newPassword)
    {
        var quotedRole = QuoteIdentifier(role.RoleName);
        var clauses = new List<string>
        {
            role.CanLogin ? "LOGIN" : "NOLOGIN",
            role.IsSuperuser ? "SUPERUSER" : "NOSUPERUSER",
            role.CanCreateDb ? "CREATEDB" : "NOCREATEDB",
            role.CanCreateRole ? "CREATEROLE" : "NOCREATEROLE",
            role.InheritsPrivileges ? "INHERIT" : "NOINHERIT",
            role.IsReplication ? "REPLICATION" : "NOREPLICATION"
        };

        if (role.ConnectionLimit >= 0)
        {
            clauses.Add($"CONNECTION LIMIT {role.ConnectionLimit}");
        }

        if (!string.IsNullOrEmpty(newPassword))
        {
            clauses.Add($"PASSWORD '{EscapeLiteral(newPassword)}'");
        }

        if (role.ValidUntil.HasValue)
        {
            clauses.Add($"VALID UNTIL '{role.ValidUntil.Value:yyyy-MM-dd HH:mm:ss}'");
        }
        else
        {
            clauses.Add("VALID UNTIL 'infinity'");
        }

        return $"ALTER ROLE {quotedRole} WITH {string.Join(" ", clauses)};";
    }

    public string GenerateDropRoleDdl(string roleName)
    {
        return $"DROP ROLE {QuoteIdentifier(roleName)};";
    }

    public async Task<QueryResult> CreateRoleAsync(IDatabaseService db, PostgresRoleInfo role, string? password)
    {
        var ddl = GenerateCreateRoleDdl(role, password);
        return await db.ExecuteQueryAsync(ddl);
    }

    public async Task<QueryResult> AlterRoleAsync(IDatabaseService db, PostgresRoleInfo role, string? newPassword)
    {
        var ddl = GenerateAlterRoleDdl(role, newPassword);
        return await db.ExecuteQueryAsync(ddl);
    }

    public async Task<QueryResult> DropRoleAsync(IDatabaseService db, string roleName)
    {
        var ddl = GenerateDropRoleDdl(roleName);
        return await db.ExecuteQueryAsync(ddl);
    }

    public async Task<IReadOnlyList<PostgresTablePrivilege>> GetTablePrivilegesAsync(IDatabaseService db, string roleName, string? schemaName = null)
    {
        // 1. Get all accessible tables
        var tables = await db.GetTablesAsync();
        var tableDict = new Dictionary<string, PostgresTablePrivilege>(StringComparer.OrdinalIgnoreCase);

        foreach (var tbl in tables)
        {
            if (!string.IsNullOrEmpty(schemaName) && !string.Equals(tbl.Schema, schemaName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = $"{tbl.Schema}.{tbl.Name}";
            tableDict[key] = new PostgresTablePrivilege
            {
                SchemaName = tbl.Schema,
                TableName = tbl.Name,
                RoleName = roleName
            };
        }

        // 2. Query existing grants for this role
        var escapedRole = EscapeLiteral(roleName);
        var schemaFilter = !string.IsNullOrEmpty(schemaName)
            ? $"AND table_schema = '{EscapeLiteral(schemaName)}'"
            : "AND table_schema NOT IN ('pg_catalog', 'information_schema')";

        var sql = $@"
SELECT 
    table_schema, 
    table_name, 
    privilege_type 
FROM information_schema.table_privileges 
WHERE grantee = '{escapedRole}'
  {schemaFilter}
ORDER BY table_schema, table_name;";

        var result = await db.ExecuteQueryAsync(sql);
        if (result.Success && result.Rows.Count > 0)
        {
            foreach (var row in result.Rows)
            {
                if (row.Count < 3) continue;

                var schema = row[0]?.ToString() ?? "public";
                var table = row[1]?.ToString() ?? string.Empty;
                var priv = row[2]?.ToString() ?? string.Empty;

                var key = $"{schema}.{table}";
                if (!tableDict.TryGetValue(key, out var privEntry))
                {
                    privEntry = new PostgresTablePrivilege
                    {
                        SchemaName = schema,
                        TableName = table,
                        RoleName = roleName
                    };
                    tableDict[key] = privEntry;
                }

                privEntry.SetPrivilege(priv, true);
            }
        }

        return tableDict.Values
            .OrderBy(p => p.SchemaName)
            .ThenBy(p => p.TableName)
            .ToList();
    }

    public string GeneratePrivilegeDdl(IReadOnlyList<PostgresPrivilegeChange> changes)
    {
        if (changes == null || changes.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // Group by Role, Schema, Table and IsGrant
        var groups = changes
            .GroupBy(c => new { c.RoleName, c.SchemaName, c.TableName, c.IsGrant });

        foreach (var g in groups)
        {
            var privs = g.Select(c => c.Privilege.ToUpperInvariant()).Distinct().ToList();
            if (privs.Count == 0) continue;

            var target = !string.IsNullOrEmpty(g.Key.SchemaName)
                ? $"{QuoteIdentifier(g.Key.SchemaName)}.{QuoteIdentifier(g.Key.TableName)}"
                : QuoteIdentifier(g.Key.TableName);

            var privList = string.Join(", ", privs);
            var role = QuoteIdentifier(g.Key.RoleName);

            if (g.Key.IsGrant)
            {
                sb.AppendLine($"GRANT {privList} ON {target} TO {role};");
            }
            else
            {
                sb.AppendLine($"REVOKE {privList} ON {target} FROM {role};");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<QueryResult> ApplyPrivilegeChangesAsync(IDatabaseService db, IReadOnlyList<PostgresPrivilegeChange> changes)
    {
        if (changes == null || changes.Count == 0)
        {
            return new QueryResult { Success = true, AffectedRows = 0 };
        }

        var ddl = GeneratePrivilegeDdl(changes);
        if (string.IsNullOrWhiteSpace(ddl))
        {
            return new QueryResult { Success = true, AffectedRows = 0 };
        }

        return await db.ExecuteQueryAsync(ddl);
    }

    public async Task<IReadOnlyList<PostgresActiveSession>> GetActiveSessionsAsync(IDatabaseService db)
    {
        int currentPid = -1;
        try
        {
            var pidRes = await db.ExecuteQueryAsync("SELECT pg_backend_pid();");
            if (pidRes.Success && pidRes.Rows.Count > 0 && pidRes.Rows[0].Count > 0)
            {
                currentPid = Convert.ToInt32(pidRes.Rows[0][0]);
            }
        }
        catch
        {
            // Fallback if permission or query fails
        }

        const string sql = @"
SELECT 
    pid,
    COALESCE(datname, '') AS datname,
    COALESCE(usename, '') AS usename,
    COALESCE(application_name, '') AS application_name,
    COALESCE(client_addr::text, '') AS client_addr,
    client_port,
    backend_start::text AS backend_start,
    query_start::text AS query_start,
    state_change::text AS state_change,
    COALESCE(state, 'idle') AS state,
    COALESCE(query, '') AS query,
    COALESCE(backend_type, 'client backend') AS backend_type,
    COALESCE(EXTRACT(EPOCH FROM (clock_timestamp() - query_start))::numeric, 0) AS elapsed_seconds
FROM pg_stat_activity
WHERE backend_type = 'client backend'
ORDER BY 
    CASE WHEN state = 'active' THEN 0 ELSE 1 END,
    elapsed_seconds DESC;";

        var result = await db.ExecuteQueryAsync(sql);
        var sessions = new List<PostgresActiveSession>();

        if (!result.Success || result.Rows.Count == 0)
        {
            return sessions;
        }

        foreach (var row in result.Rows)
        {
            if (row.Count < 13) continue;

            int pid = ConvertToInt(row[0], 0);
            double elapsed = ConvertToDouble(row[12], 0.0);

            var session = new PostgresActiveSession
            {
                Pid = pid,
                DatabaseName = row[1]?.ToString() ?? string.Empty,
                UserName = row[2]?.ToString() ?? string.Empty,
                ApplicationName = row[3]?.ToString() ?? string.Empty,
                ClientAddress = row[4]?.ToString() ?? string.Empty,
                ClientPort = row[5] != null && int.TryParse(row[5]?.ToString(), out var port) ? port : null,
                BackendStart = ParseDateTime(row[6]?.ToString()),
                QueryStart = ParseDateTime(row[7]?.ToString()),
                StateChange = ParseDateTime(row[8]?.ToString()),
                State = row[9]?.ToString() ?? "idle",
                QueryText = row[10]?.ToString() ?? string.Empty,
                BackendType = row[11]?.ToString() ?? "client backend",
                ElapsedSeconds = elapsed,
                IsCurrentSession = (pid > 0 && pid == currentPid)
            };

            sessions.Add(session);
        }

        return sessions;
    }

    public async Task<bool> CancelQueryAsync(IDatabaseService db, int pid)
    {
        var sql = $"SELECT pg_cancel_backend({pid});";
        var res = await db.ExecuteQueryAsync(sql);
        if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0)
        {
            return ConvertToBool(res.Rows[0][0]);
        }
        return false;
    }

    public async Task<bool> TerminateBackendAsync(IDatabaseService db, int pid)
    {
        var sql = $"SELECT pg_terminate_backend({pid});";
        var res = await db.ExecuteQueryAsync(sql);
        if (res.Success && res.Rows.Count > 0 && res.Rows[0].Count > 0)
        {
            return ConvertToBool(res.Rows[0][0]);
        }
        return false;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static string EscapeLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static bool ConvertToBool(object? value)
    {
        if (value == null || value == DBNull.Value) return false;
        if (value is bool b) return b;
        var s = value.ToString()?.Trim().ToLowerInvariant();
        return s == "true" || s == "t" || s == "1" || s == "yes";
    }

    private static int ConvertToInt(object? value, int fallback)
    {
        if (value == null || value == DBNull.Value) return fallback;
        if (int.TryParse(value.ToString(), out var i)) return i;
        return fallback;
    }

    private static double ConvertToDouble(object? value, double fallback)
    {
        if (value == null || value == DBNull.Value) return fallback;
        if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        if (double.TryParse(value.ToString(), out var d2)) return d2;
        return fallback;
    }

    private static DateTime? ParseValidUntil(string? str)
    {
        if (string.IsNullOrWhiteSpace(str) || string.Equals(str, "infinity", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        if (DateTime.TryParse(str, out var dtLocal))
        {
            return dtLocal;
        }

        return null;
    }

    private static DateTime? ParseDateTime(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        if (DateTime.TryParse(str, out var dtLocal))
        {
            return dtLocal;
        }

        return null;
    }
}
