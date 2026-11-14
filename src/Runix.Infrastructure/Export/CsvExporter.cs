using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador de dados para formato CSV / TSV / Delimitado.
/// </summary>
public static class CsvExporter
{
    public static async Task<long> ExportAsync(
        Stream outputStream,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var encoding = new UTF8Encoding(options.UseBom);
        using var writer = new StreamWriter(outputStream, encoding, bufferSize: 65536, leaveOpen: true);
        var delimiter = options.GetDelimiterChar();
        long rowCount = 0;

        if (options.IncludeHeader && columns.Count > 0)
        {
            await WriteRowAsync(writer, columns, delimiter);
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var stringValues = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var val = i < row.Count ? row[i] : null;
                stringValues[i] = FormatValue(val);
            }

            await WriteRowAsync(writer, stringValues, delimiter);
            rowCount++;

            if (rowCount % 100 == 0)
            {
                progress?.Report((int)rowCount);
            }
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
        var encoding = new UTF8Encoding(options.UseBom);
        using var writer = new StreamWriter(outputStream, encoding, bufferSize: 65536, leaveOpen: true);
        var delimiter = options.GetDelimiterChar();
        long rowCount = 0;

        int fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        if (options.IncludeHeader && fieldCount > 0)
        {
            await WriteRowAsync(writer, columns, delimiter);
        }

        var values = new object[fieldCount];
        var stringValues = new string[fieldCount];

        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            reader.GetValues(values);

            for (int i = 0; i < fieldCount; i++)
            {
                var val = values[i] == DBNull.Value ? null : values[i];
                stringValues[i] = FormatValue(val);
            }

            await WriteRowAsync(writer, stringValues, delimiter);
            rowCount++;

            if (rowCount % 100 == 0)
            {
                progress?.Report((int)rowCount);
            }
        }

        await writer.FlushAsync(ct);
        progress?.Report((int)rowCount);
        return rowCount;
    }

    private static async Task WriteRowAsync(StreamWriter writer, IReadOnlyList<string> values, char delimiter)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(delimiter);
            }
            await writer.WriteAsync(EscapeField(values[i], delimiter));
        }
        await writer.WriteLineAsync();
    }

    public static string EscapeField(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value)) return "";

        bool needsQuotes = value.Contains(delimiter) ||
                           value.Contains('"') ||
                           value.Contains('\n') ||
                           value.Contains('\r');

        if (!needsQuotes) return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public static string FormatValue(object? val)
    {
        if (val == null || val == DBNull.Value) return "";

        return val switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
            bool b => b ? "true" : "false",
            byte[] bytes => Convert.ToHexString(bytes),
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => val.ToString() ?? ""
        };
    }
}
