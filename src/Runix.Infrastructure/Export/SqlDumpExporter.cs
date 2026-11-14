using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador de dados para script SQL de carga/dump com lotes transacionais e escape por dialeto.
/// </summary>
public static class SqlDumpExporter
{
    public static async Task<long> ExportAsync(
        Stream outputStream,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), bufferSize: 65536, leaveOpen: true);
        long rowCount = 0;
        var isPostgres = options.TargetDatabaseType == DatabaseType.PostgreSQL;
        var tableName = EscapeIdentifier(string.IsNullOrWhiteSpace(options.TableName) ? "exported_data" : options.TableName, isPostgres);

        // Header comments
        await writer.WriteLineAsync($"-- RUNIX Database Studio SQL Dump");
        await writer.WriteLineAsync($"-- Target: {options.TargetDatabaseType}");
        await writer.WriteLineAsync($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteLineAsync();

        // Transaction Begin
        if (options.IncludeTransaction)
        {
            await writer.WriteLineAsync(isPostgres ? "BEGIN;" : "BEGIN TRANSACTION;");
            await writer.WriteLineAsync();
        }

        // Optional CREATE TABLE DDL
        if (options.IncludeCreateTable && columns.Count > 0)
        {
            await writer.WriteLineAsync($"CREATE TABLE IF NOT EXISTS {tableName} (");
            for (int i = 0; i < columns.Count; i++)
            {
                var colName = EscapeIdentifier(columns[i], isPostgres);
                var comma = i < columns.Count - 1 ? "," : "";
                await writer.WriteLineAsync($"    {colName} TEXT{comma}");
            }
            await writer.WriteLineAsync(");");
            await writer.WriteLineAsync();
        }

        // Column List
        var columnList = string.Join(", ", columns.Select(c => EscapeIdentifier(c, isPostgres)));
        var batchSize = Math.Max(1, options.BatchSize);
        var currentBatch = new List<string>(batchSize);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var tupleValues = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var val = i < row.Count ? row[i] : null;
                tupleValues[i] = FormatSqlLiteral(val, isPostgres);
            }

            currentBatch.Add($"({string.Join(", ", tupleValues)})");
            rowCount++;

            if (currentBatch.Count >= batchSize)
            {
                await WriteBatchAsync(writer, tableName, columnList, currentBatch);
                currentBatch.Clear();
                progress?.Report((int)rowCount);
            }
        }

        if (currentBatch.Count > 0)
        {
            await WriteBatchAsync(writer, tableName, columnList, currentBatch);
            currentBatch.Clear();
        }

        // Transaction Commit
        if (options.IncludeTransaction)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("COMMIT;");
        }

        await writer.FlushAsync(ct);
        progress?.Report((int)rowCount);
        return rowCount;
    }

    public static async Task<long> ExportFromReaderAsync(
        Stream outputStream,
        DbDataReader reader,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), bufferSize: 65536, leaveOpen: true);
        long rowCount = 0;
        var isPostgres = options.TargetDatabaseType == DatabaseType.PostgreSQL;
        var tableName = EscapeIdentifier(string.IsNullOrWhiteSpace(options.TableName) ? "exported_data" : options.TableName, isPostgres);

        int fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        // Header comments
        await writer.WriteLineAsync($"-- RUNIX Database Studio SQL Dump");
        await writer.WriteLineAsync($"-- Target: {options.TargetDatabaseType}");
        await writer.WriteLineAsync($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteLineAsync();

        // Transaction Begin
        if (options.IncludeTransaction)
        {
            await writer.WriteLineAsync(isPostgres ? "BEGIN;" : "BEGIN TRANSACTION;");
            await writer.WriteLineAsync();
        }

        // Optional CREATE TABLE DDL
        if (options.IncludeCreateTable && fieldCount > 0)
        {
            await writer.WriteLineAsync($"CREATE TABLE IF NOT EXISTS {tableName} (");
            for (int i = 0; i < fieldCount; i++)
            {
                var colName = EscapeIdentifier(columns[i], isPostgres);
                var comma = i < fieldCount - 1 ? "," : "";
                await writer.WriteLineAsync($"    {colName} TEXT{comma}");
            }
            await writer.WriteLineAsync(");");
            await writer.WriteLineAsync();
        }

        var columnList = string.Join(", ", columns.Select(c => EscapeIdentifier(c, isPostgres)));
        var batchSize = Math.Max(1, options.BatchSize);
        var currentBatch = new List<string>(batchSize);
        var values = new object[fieldCount];

        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            reader.GetValues(values);

            var tupleValues = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                var val = values[i] == DBNull.Value ? null : values[i];
                tupleValues[i] = FormatSqlLiteral(val, isPostgres);
            }

            currentBatch.Add($"({string.Join(", ", tupleValues)})");
            rowCount++;

            if (currentBatch.Count >= batchSize)
            {
                await WriteBatchAsync(writer, tableName, columnList, currentBatch);
                currentBatch.Clear();
                progress?.Report((int)rowCount);
            }
        }

        if (currentBatch.Count > 0)
        {
            await WriteBatchAsync(writer, tableName, columnList, currentBatch);
            currentBatch.Clear();
        }

        // Transaction Commit
        if (options.IncludeTransaction)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("COMMIT;");
        }

        await writer.FlushAsync(ct);
        progress?.Report((int)rowCount);
        return rowCount;
    }

    private static async Task WriteBatchAsync(StreamWriter writer, string tableName, string columnList, List<string> batch)
    {
        await writer.WriteLineAsync($"INSERT INTO {tableName} ({columnList}) VALUES");
        for (int i = 0; i < batch.Count; i++)
        {
            var isLast = i == batch.Count - 1;
            var sep = isLast ? ";" : ",";
            await writer.WriteLineAsync($"  {batch[i]}{sep}");
        }
    }

    public static string EscapeIdentifier(string identifier, bool isPostgres)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return "\"\"";
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    public static string FormatSqlLiteral(object? val, bool isPostgres)
    {
        if (val == null || val == DBNull.Value) return "NULL";

        return val switch
        {
            bool b => isPostgres ? (b ? "TRUE" : "FALSE") : (b ? "1" : "0"),
            sbyte or byte or short or ushort or int or uint or long or ulong =>
                Convert.ToString(val, CultureInfo.InvariantCulture)!,
            float f => f.ToString("G9", CultureInfo.InvariantCulture),
            double d => d.ToString("G17", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.fff zzz}'",
            byte[] bytes => isPostgres
                ? $"'\\x{Convert.ToHexString(bytes)}'"
                : $"X'{Convert.ToHexString(bytes)}'",
            _ => $"'{val.ToString()?.Replace("'", "''")}'"
        };
    }
}
