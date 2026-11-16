using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Storage;

/// <summary>
/// Serviço de armazenamento local da aplicação persistido em SQLite (runix_app.db).
/// </summary>
public class AppStorageService : IAppStorageService
{
    private static readonly Lazy<AppStorageService> _lazyInstance = new(() => new AppStorageService());
    public static AppStorageService Instance => _lazyInstance.Value;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    public AppStorageService(string? customDbPath = null)
    {
        string dbPath;
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            dbPath = customDbPath;
        }
        else
        {
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RUNIX");
            Directory.CreateDirectory(configDir);
            dbPath = Path.Combine(configDir, "runix_app.db");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var ddl = @"
CREATE TABLE IF NOT EXISTS saved_queries (
    id TEXT PRIMARY KEY,
    connection_id TEXT,
    title TEXT NOT NULL,
    description TEXT,
    sql_text TEXT NOT NULL,
    tags TEXT,
    is_favorite INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_saved_queries_fav ON saved_queries (is_favorite, updated_at DESC);

CREATE TABLE IF NOT EXISTS custom_snippets (
    id TEXT PRIMARY KEY,
    prefix TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL,
    description TEXT,
    body TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_custom_snippets_prefix ON custom_snippets (prefix);

CREATE TABLE IF NOT EXISTS query_history (
    id TEXT PRIMARY KEY,
    connection_id TEXT,
    sql_text TEXT NOT NULL,
    elapsed_ms INTEGER NOT NULL DEFAULT 0,
    row_count INTEGER,
    success INTEGER NOT NULL DEFAULT 1,
    error_message TEXT,
    executed_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_query_history_date ON query_history (executed_at DESC);

CREATE TABLE IF NOT EXISTS workspace_sessions (
    connection_key TEXT PRIMARY KEY,
    active_tab_index INTEGER NOT NULL DEFAULT 0,
    session_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_workspace_sessions_key ON workspace_sessions (connection_key);
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            await InitializeAsync(ct);
        }
    }

    // ==========================================
    // QUERIES SALVAS
    // ==========================================

    public async Task<IReadOnlyList<SavedQuery>> GetSavedQueriesAsync(
        string? connectionId = null,
        string? searchFilter = null,
        bool? onlyFavorites = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var list = new List<SavedQuery>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = "SELECT id, connection_id, title, description, sql_text, tags, is_favorite, created_at, updated_at FROM saved_queries WHERE 1=1";
        using var cmd = conn.CreateCommand();

        if (onlyFavorites == true)
        {
            sql += " AND is_favorite = 1";
        }

        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            sql += " AND (connection_id IS NULL OR connection_id = @connId)";
            cmd.Parameters.AddWithValue("@connId", connectionId);
        }

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            sql += " AND (title LIKE @filter OR description LIKE @filter OR sql_text LIKE @filter OR tags LIKE @filter)";
            cmd.Parameters.AddWithValue("@filter", $"%{searchFilter.Trim()}%");
        }

        sql += " ORDER BY is_favorite DESC, updated_at DESC;";
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tagsRaw = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var tagsList = tagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim())
                                  .Where(t => !string.IsNullOrEmpty(t))
                                  .ToList();

            list.Add(new SavedQuery
            {
                Id = reader.GetString(0),
                ConnectionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                SqlText = reader.GetString(4),
                Tags = tagsList,
                IsFavorite = reader.GetInt32(6) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(7)),
                UpdatedAt = DateTime.Parse(reader.GetString(8))
            });
        }

        return list;
    }

    public async Task<SavedQuery?> GetSavedQueryByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, connection_id, title, description, sql_text, tags, is_favorite, created_at, updated_at FROM saved_queries WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var tagsRaw = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var tagsList = tagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim())
                                  .ToList();

            return new SavedQuery
            {
                Id = reader.GetString(0),
                ConnectionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Title = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                SqlText = reader.GetString(4),
                Tags = tagsList,
                IsFavorite = reader.GetInt32(6) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(7)),
                UpdatedAt = DateTime.Parse(reader.GetString(8))
            };
        }

        return null;
    }

    public async Task SaveQueryAsync(SavedQuery query, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO saved_queries (id, connection_id, title, description, sql_text, tags, is_favorite, created_at, updated_at)
VALUES (@id, @connectionId, @title, @description, @sqlText, @tags, @isFavorite, @createdAt, @updatedAt)
ON CONFLICT(id) DO UPDATE SET
    connection_id = excluded.connection_id,
    title = excluded.title,
    description = excluded.description,
    sql_text = excluded.sql_text,
    tags = excluded.tags,
    is_favorite = excluded.is_favorite,
    updated_at = excluded.updated_at;
";
        query.UpdatedAt = DateTime.UtcNow;

        cmd.Parameters.AddWithValue("@id", query.Id);
        cmd.Parameters.AddWithValue("@connectionId", (object?)query.ConnectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", query.Title);
        cmd.Parameters.AddWithValue("@description", (object?)query.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sqlText", query.SqlText);
        cmd.Parameters.AddWithValue("@tags", string.Join(",", query.Tags));
        cmd.Parameters.AddWithValue("@isFavorite", query.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdAt", query.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updatedAt", query.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ToggleFavoriteAsync(string queryId, bool isFavorite, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE saved_queries SET is_favorite = @fav, updated_at = @now WHERE id = @id;";
        cmd.Parameters.AddWithValue("@fav", isFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", queryId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteSavedQueryAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM saved_queries WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ==========================================
    // CUSTOM SNIPPETS
    // ==========================================

    public async Task<IReadOnlyList<CustomSnippet>> GetCustomSnippetsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var list = new List<CustomSnippet>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, prefix, title, description, body, created_at, updated_at FROM custom_snippets ORDER BY prefix ASC;";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CustomSnippet
            {
                Id = reader.GetString(0),
                Prefix = reader.GetString(1),
                Title = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                Body = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                UpdatedAt = DateTime.Parse(reader.GetString(6))
            });
        }

        return list;
    }

    public async Task SaveCustomSnippetAsync(CustomSnippet snippet, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO custom_snippets (id, prefix, title, description, body, created_at, updated_at)
VALUES (@id, @prefix, @title, @description, @body, @createdAt, @updatedAt)
ON CONFLICT(prefix) DO UPDATE SET
    title = excluded.title,
    description = excluded.description,
    body = excluded.body,
    updated_at = excluded.updated_at;
";
        snippet.UpdatedAt = DateTime.UtcNow;

        cmd.Parameters.AddWithValue("@id", snippet.Id);
        cmd.Parameters.AddWithValue("@prefix", snippet.Prefix.Trim());
        cmd.Parameters.AddWithValue("@title", snippet.Title.Trim());
        cmd.Parameters.AddWithValue("@description", (object?)snippet.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body", snippet.Body);
        cmd.Parameters.AddWithValue("@createdAt", snippet.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updatedAt", snippet.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteCustomSnippetAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM custom_snippets WHERE id = @id OR prefix = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ==========================================
    // QUERY HISTORY
    // ==========================================

    public async Task LogQueryExecutionAsync(QueryHistoryEntry entry, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO query_history (id, connection_id, sql_text, elapsed_ms, row_count, success, error_message, executed_at)
VALUES (@id, @connId, @sql, @elapsed, @rowCount, @success, @error, @executedAt);
";
        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.Parameters.AddWithValue("@connId", (object?)entry.ConnectionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sql", entry.SqlText);
        cmd.Parameters.AddWithValue("@elapsed", entry.ElapsedMilliseconds);
        cmd.Parameters.AddWithValue("@rowCount", (object?)entry.RowCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@success", entry.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("@error", (object?)entry.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@executedAt", entry.ExecutedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<QueryHistoryEntry>> GetQueryHistoryAsync(string? connectionId = null, int limit = 50, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var list = new List<QueryHistoryEntry>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        var sql = "SELECT id, connection_id, sql_text, elapsed_ms, row_count, success, error_message, executed_at FROM query_history WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            sql += " AND (connection_id IS NULL OR connection_id = @connId)";
            cmd.Parameters.AddWithValue("@connId", connectionId);
        }

        sql += " ORDER BY executed_at DESC LIMIT @limit;";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new QueryHistoryEntry
            {
                Id = reader.GetString(0),
                ConnectionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                SqlText = reader.GetString(2),
                ElapsedMilliseconds = reader.GetInt64(3),
                RowCount = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                Success = reader.GetInt32(5) == 1,
                ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                ExecutedAt = DateTime.Parse(reader.GetString(7))
            });
        }

        return list;
    }

    public async Task ClearQueryHistoryAsync(string? connectionId = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            cmd.CommandText = "DELETE FROM query_history WHERE connection_id = @connId;";
            cmd.Parameters.AddWithValue("@connId", connectionId);
        }
        else
        {
            cmd.CommandText = "DELETE FROM query_history;";
        }

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ==========================================
    // PERSISTÊNCIA DE SESSÃO DO WORKSPACE (v1.2.0)
    // ==========================================

    public async Task<WorkspaceSessionState?> GetWorkspaceSessionAsync(string connectionKey, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var sql = "SELECT session_json FROM workspace_sessions WHERE connection_key = @key;";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@key", connectionKey);

            var json = (string?)await cmd.ExecuteScalarAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WorkspaceSessionState>(json);
        }
        catch
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveWorkspaceSessionAsync(WorkspaceSessionState session, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var json = JsonSerializer.Serialize(session);
            var sql = @"
INSERT INTO workspace_sessions (connection_key, active_tab_index, session_json, updated_at)
VALUES (@key, @tab_idx, @json, @updated_at)
ON CONFLICT(connection_key) DO UPDATE SET
    active_tab_index = excluded.active_tab_index,
    session_json = excluded.session_json,
    updated_at = excluded.updated_at;
";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@key", session.ConnectionKey);
            cmd.Parameters.AddWithValue("@tab_idx", session.ActiveTabIndex);
            cmd.Parameters.AddWithValue("@json", json);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearWorkspaceSessionAsync(string connectionKey, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            var sql = "DELETE FROM workspace_sessions WHERE connection_key = @key;";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@key", connectionKey);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }
}
