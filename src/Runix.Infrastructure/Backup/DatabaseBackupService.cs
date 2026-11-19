using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;
using Runix.Infrastructure.Database;

namespace Runix.Infrastructure.Backup;

public class DatabaseBackupService : IDatabaseBackupService
{
    private readonly IDatabaseService _databaseService;
    private readonly IProgrammabilityService _programmabilityService;
    private readonly DdlGeneratorService _ddlGenerator = new();

    public DatabaseBackupService(IDatabaseService databaseService, IProgrammabilityService? programmabilityService = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _programmabilityService = programmabilityService ?? new ProgrammabilityService(databaseService);
    }

    public async Task<BackupResult> CreateBackupAsync(
        BackupOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (options == null || string.IsNullOrWhiteSpace(options.OutputFilePath))
        {
            return new BackupResult { Success = false, ErrorMessage = "Caminho do arquivo de backup é obrigatório." };
        }

        try
        {
            var outDir = Path.GetDirectoryName(options.OutputFilePath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            // 1. SQLite Online Binary Backup (.db)
            if (options.Format == BackupFormat.SqliteBinary)
            {
                if (_databaseService.Config.Type != DatabaseType.SQLite)
                {
                    return new BackupResult
                    {
                        Success = false,
                        ErrorMessage = "Backup binário (.db) é suportado exclusivamente para bancos SQLite."
                    };
                }

                await CreateSqliteBinaryBackupAsync(options.OutputFilePath, cancellationToken);
                sw.Stop();
                return new BackupResult
                {
                    Success = true,
                    FilePath = options.OutputFilePath,
                    TablesProcessed = 1,
                    ElapsedMilliseconds = sw.ElapsedMilliseconds
                };
            }

            // 2. SQL Script (.sql) or Compressed GZip (.sql.gz)
            await using var fileStream = new FileStream(options.OutputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            Stream targetStream = options.Format == BackupFormat.CompressedSqlGzip
                ? new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: false)
                : fileStream;

            await using (targetStream)
            await using (var writer = new StreamWriter(targetStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                var result = await WriteSqlBackupToStreamAsync(writer, options, progress, cancellationToken);
                await writer.FlushAsync(cancellationToken);
                sw.Stop();
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                result.FilePath = options.OutputFilePath;
                return result;
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BackupResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FilePath = options.OutputFilePath,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<BackupResult> RestoreBackupAsync(
        RestoreOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (options == null || string.IsNullOrWhiteSpace(options.InputFilePath) || !File.Exists(options.InputFilePath))
        {
            return new BackupResult { Success = false, ErrorMessage = "Arquivo de backup não encontrado." };
        }

        try
        {
            var ext = Path.GetExtension(options.InputFilePath).ToLowerInvariant();
            if (ext is ".db" or ".sqlite" or ".sqlite3")
            {
                if (_databaseService.Config.Type != DatabaseType.SQLite)
                {
                    return new BackupResult
                    {
                        Success = false,
                        ErrorMessage = "Arquivos binários SQLite (.db) só podem ser restaurados em conexões SQLite."
                    };
                }

                await RestoreSqliteBinaryBackupAsync(options.InputFilePath, cancellationToken);
                sw.Stop();
                return new BackupResult
                {
                    Success = true,
                    FilePath = options.InputFilePath,
                    StatementsExecuted = 1,
                    ElapsedMilliseconds = sw.ElapsedMilliseconds
                };
            }

            // Read .sql or .sql.gz
            string sqlContent;
            await using (var fileStream = new FileStream(options.InputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Stream readStream = options.InputFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? new GZipStream(fileStream, CompressionMode.Decompress)
                    : fileStream;

                await using (readStream)
                using (var reader = new StreamReader(readStream, Encoding.UTF8))
                {
                    sqlContent = await reader.ReadToEndAsync(cancellationToken);
                }
            }

            var statements = SplitSqlStatements(sqlContent);
            if (statements.Count == 0)
            {
                return new BackupResult
                {
                    Success = false,
                    ErrorMessage = "Nenhuma instrução SQL executável encontrada no arquivo de backup."
                };
            }

            progress?.Report(new BackupProgress
            {
                CurrentPhase = $"Executando {statements.Count} instruções SQL transacionalmente...",
                ProcessedTables = 0,
                TotalTables = statements.Count
            });

            var commands = statements.Select(s => new ParameterizedCommand(s)).ToList();
            var execRes = await _databaseService.ExecuteBatchTransactionalAsync(commands, cancellationToken);
            sw.Stop();

            if (!execRes.Success)
            {
                return new BackupResult
                {
                    Success = false,
                    ErrorMessage = execRes.ErrorMessage,
                    FilePath = options.InputFilePath,
                    ElapsedMilliseconds = sw.ElapsedMilliseconds
                };
            }

            progress?.Report(new BackupProgress
            {
                CurrentPhase = "Restore concluído com sucesso!",
                ProcessedTables = statements.Count,
                TotalTables = statements.Count
            });

            return new BackupResult
            {
                Success = true,
                FilePath = options.InputFilePath,
                StatementsExecuted = statements.Count,
                RowsProcessed = execRes.RowsAffected,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BackupResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FilePath = options.InputFilePath,
                ElapsedMilliseconds = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<BackupResult> WriteSqlBackupToStreamAsync(
        StreamWriter writer,
        BackupOptions options,
        IProgress<BackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var allTables = await _databaseService.GetTablesAsync();
        var physicalTables = allTables.Where(t => !t.IsView).Select(t => t.Name).ToList();

        if (options.SelectedTables != null && options.SelectedTables.Count > 0)
        {
            var selectedSet = new HashSet<string>(options.SelectedTables, StringComparer.OrdinalIgnoreCase);
            physicalTables = physicalTables.Where(t => selectedSet.Contains(t)).ToList();
        }

        var foreignKeys = await _databaseService.GetForeignKeysAsync();
        var orderedTables = SortTablesTopologically(physicalTables, foreignKeys);

        await writer.WriteLineAsync($"-- =========================================================");
        await writer.WriteLineAsync($"-- RUNIX Database Studio Native Backup");
        await writer.WriteLineAsync($"-- Engine: {_databaseService.Config.Type}");
        await writer.WriteLineAsync($"-- Scope:  {options.Scope}");
        await writer.WriteLineAsync($"-- Date:   {DateTime.UtcNow:O}");
        await writer.WriteLineAsync($"-- =========================================================");
        await writer.WriteLineAsync();

        int tablesDone = 0;
        long totalRows = 0;
        int totalStatements = 0;

        // 1. Optional DROP statements in REVERSE topological order (children before parents)
        if (options.IncludeDropStatements && options.Scope is BackupScope.Full or BackupScope.SchemaOnly)
        {
            for (int i = orderedTables.Count - 1; i >= 0; i--)
            {
                var tbl = orderedTables[i];
                var cascade = _databaseService.Config.Type == DatabaseType.PostgreSQL ? " CASCADE" : "";
                await writer.WriteLineAsync($"DROP TABLE IF EXISTS {QuoteIdent(tbl)}{cascade};");
                totalStatements++;
            }
            await writer.WriteLineAsync();
        }

        // 2. Table DDL + Data in forward topological order (parents before children)
        foreach (var tableName in orderedTables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BackupProgress
            {
                CurrentPhase = $"Processando tabela '{tableName}'...",
                ProcessedTables = tablesDone,
                TotalTables = orderedTables.Count,
                ProcessedRows = totalRows
            });

            var cols = await _databaseService.GetTableColumnsAsync(tableName);

            if (options.Scope is BackupScope.Full or BackupScope.SchemaOnly)
            {
                string createDdl;
                if (_databaseService.Config.Type == DatabaseType.SQLite)
                {
                    var rawDdl = await _databaseService.GetTableDdlAsync(tableName);
                    // Extract only the CREATE TABLE part (indexes are emitted in programmability section)
                    var idxSplit = rawDdl.IndexOf("-- Indexes", StringComparison.Ordinal);
                    createDdl = (idxSplit >= 0 ? rawDdl[..idxSplit] : rawDdl).Trim();
                    if (!createDdl.EndsWith(';')) createDdl += ";";
                }
                else
                {
                    var node = new ErdTableNode
                    {
                        Table = new TableInfo { Name = tableName, Schema = "public" },
                        Columns = cols
                    };
                    createDdl = _ddlGenerator.GenerateCreateTableSql(node, _databaseService.Config.Type);
                }

                await writer.WriteLineAsync($"-- Table: {tableName}");
                await writer.WriteLineAsync(createDdl);
                await writer.WriteLineAsync();
                totalStatements++;
            }

            if (options.Scope is BackupScope.Full or BackupScope.DataOnly)
            {
                var dataRes = await _databaseService.ExecuteQueryAsync($"SELECT * FROM {QuoteIdent(tableName)};", 100_000);
                if (dataRes.Success && dataRes.Rows.Count > 0)
                {
                    var colList = string.Join(", ", dataRes.Columns.Select(QuoteIdent));
                    foreach (var row in dataRes.Rows)
                    {
                        var values = string.Join(", ", row.Select(FormatSqlLiteral));
                        await writer.WriteLineAsync($"INSERT INTO {QuoteIdent(tableName)} ({colList}) VALUES ({values});");
                        totalRows++;
                        totalStatements++;
                    }
                    await writer.WriteLineAsync();
                }
            }

            tablesDone++;
        }

        // 3. Programmable Objects (Views, Triggers, Functions, Indexes)
        if (options.IncludeProgrammableObjects && options.Scope is BackupScope.Full or BackupScope.SchemaOnly)
        {
            var progObjects = await _programmabilityService.GetProgrammableObjectsAsync(cancellationToken: cancellationToken);
            if (progObjects.Count > 0)
            {
                await writer.WriteLineAsync("-- Programmable Objects (Views, Triggers, Functions, Indexes)");
                foreach (var obj in progObjects)
                {
                    if (string.IsNullOrWhiteSpace(obj.DefinitionDdl)) continue;
                    var ddl = obj.DefinitionDdl.Trim();
                    if (!ddl.EndsWith(';')) ddl += ";";
                    await writer.WriteLineAsync(ddl);
                    await writer.WriteLineAsync();
                    totalStatements++;
                }
            }
        }

        progress?.Report(new BackupProgress
        {
            CurrentPhase = "Backup concluído!",
            ProcessedTables = orderedTables.Count,
            TotalTables = orderedTables.Count,
            ProcessedRows = totalRows
        });

        return new BackupResult
        {
            Success = true,
            TablesProcessed = orderedTables.Count,
            RowsProcessed = totalRows,
            StatementsExecuted = totalStatements
        };
    }

    private async Task CreateSqliteBinaryBackupAsync(string targetFilePath, CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(_databaseService.Config.FilePath);
        var destPath = Path.GetFullPath(targetFilePath);

        if (File.Exists(destPath))
            File.Delete(destPath);

        await using var sourceConn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString());
        await using var destConn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());

        await sourceConn.OpenAsync(cancellationToken);
        await destConn.OpenAsync(cancellationToken);

        sourceConn.BackupDatabase(destConn);
    }

    private async Task RestoreSqliteBinaryBackupAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        var sourceBackupPath = Path.GetFullPath(backupFilePath);
        var activeDbPath = Path.GetFullPath(_databaseService.Config.FilePath);

        await using var backupConn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourceBackupPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var destConn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = activeDbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());

        await backupConn.OpenAsync(cancellationToken);
        await destConn.OpenAsync(cancellationToken);

        backupConn.BackupDatabase(destConn);
    }

    public IReadOnlyList<string> SortTablesTopologically(
        IReadOnlyList<string> tableNames,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        if (tableNames == null || tableNames.Count <= 1)
            return tableNames ?? Array.Empty<string>();

        var tableSet = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        var canonicalName = tableNames.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(t => t, t => t, StringComparer.OrdinalIgnoreCase);

        // Graph: parent -> list of children that depend on parent
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tableNames)
        {
            dependents[t] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            inDegree[t] = 0;
        }

        if (foreignKeys != null)
        {
            foreach (var fk in foreignKeys)
            {
                if (string.IsNullOrWhiteSpace(fk.SourceTable) || string.IsNullOrWhiteSpace(fk.TargetTable))
                    continue;

                // Ignore self-referencing FKs or FKs outside the selected table set
                if (string.Equals(fk.SourceTable, fk.TargetTable, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!tableSet.Contains(fk.SourceTable) || !tableSet.Contains(fk.TargetTable))
                    continue;

                var child = canonicalName[fk.SourceTable];
                var parent = canonicalName[fk.TargetTable];

                if (dependents[parent].Add(child))
                {
                    inDegree[child]++;
                }
            }
        }

        var queue = new SortedSet<string>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);

        var sorted = new List<string>(tableNames.Count);

        while (queue.Count > 0)
        {
            var current = queue.Min!;
            queue.Remove(current);
            sorted.Add(current);

            foreach (var child in dependents[current])
            {
                inDegree[child]--;
                if (inDegree[child] == 0)
                {
                    queue.Add(child);
                }
            }
        }

        // If any cycle existed, append remaining tables deterministically
        if (sorted.Count < tableNames.Count)
        {
            foreach (var t in tableNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!sorted.Contains(t, StringComparer.OrdinalIgnoreCase))
                    sorted.Add(t);
            }
        }

        return sorted;
    }

    public IReadOnlyList<string> SplitSqlStatements(string sqlScript)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sqlScript))
            return statements;

        var current = new StringBuilder();
        int i = 0;
        int len = sqlScript.Length;
        int triggerBeginDepth = 0;
        bool seenTriggerKeywordInCurrentStmt = false;

        while (i < len)
        {
            char c = sqlScript[i];

            // 1. Line comment: --
            if (c == '-' && i + 1 < len && sqlScript[i + 1] == '-')
            {
                int start = i;
                i += 2;
                while (i < len && sqlScript[i] != '\n')
                    i++;
                if (i < len) i++; // include newline
                continue;
            }

            // 2. Block comment: /* ... */
            if (c == '/' && i + 1 < len && sqlScript[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(sqlScript[i] == '*' && sqlScript[i + 1] == '/'))
                    i++;
                i = i + 1 < len ? i + 2 : len;
                continue;
            }

            // 3. Single-quoted string: '...'
            if (c == '\'')
            {
                int start = i++;
                while (i < len)
                {
                    if (sqlScript[i] == '\'')
                    {
                        if (i + 1 < len && sqlScript[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                current.Append(sqlScript.AsSpan(start, i - start));
                continue;
            }

            // 4. Double-quoted identifier: "..."
            if (c == '"')
            {
                int start = i++;
                while (i < len)
                {
                    if (sqlScript[i] == '"')
                    {
                        if (i + 1 < len && sqlScript[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                current.Append(sqlScript.AsSpan(start, i - start));
                continue;
            }

            // 5. Dollar-quoted block: $$...$$ or $tag$...$tag$
            if (c == '$')
            {
                int tagEnd = i + 1;
                while (tagEnd < len && (char.IsLetterOrDigit(sqlScript[tagEnd]) || sqlScript[tagEnd] == '_'))
                    tagEnd++;

                if (tagEnd < len && sqlScript[tagEnd] == '$')
                {
                    string delim = sqlScript[i..(tagEnd + 1)];
                    int closePos = sqlScript.IndexOf(delim, tagEnd + 1, StringComparison.Ordinal);
                    int nextIdx = closePos >= 0 ? closePos + delim.Length : len;
                    current.Append(sqlScript.AsSpan(i, nextIdx - i));
                    i = nextIdx;
                    continue;
                }
            }

            // 6. Track TRIGGER BEGIN ... END blocks so internal semicolons don't split the trigger
            if (char.IsLetter(c) && (i == 0 || !char.IsLetterOrDigit(sqlScript[i - 1])))
            {
                int wordStart = i;
                while (i < len && (char.IsLetterOrDigit(sqlScript[i]) || sqlScript[i] == '_'))
                    i++;

                var word = sqlScript[wordStart..i];
                current.Append(word);

                if (word.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase))
                {
                    seenTriggerKeywordInCurrentStmt = true;
                }
                else if (seenTriggerKeywordInCurrentStmt && word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    triggerBeginDepth++;
                }
                else if (triggerBeginDepth > 0 && word.Equals("END", StringComparison.OrdinalIgnoreCase))
                {
                    triggerBeginDepth--;
                }
                continue;
            }

            // 7. Top-level semicolon
            if (c == ';' && triggerBeginDepth == 0)
            {
                current.Append(';');
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt) && stmt != ";")
                {
                    statements.Add(stmt);
                }
                current.Clear();
                seenTriggerKeywordInCurrentStmt = false;
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        var trailing = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(trailing) && trailing != ";")
        {
            statements.Add(trailing);
        }

        return statements;
    }

    private static string QuoteIdent(string ident)
    {
        return $"\"{ident.Replace("\"", "\"\"")}\"";
    }

    private static string FormatSqlLiteral(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            bool b => b ? "1" : "0",
            byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            float or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            _ => $"'{value.ToString()?.Replace("'", "''")}'"
        };
    }
}
