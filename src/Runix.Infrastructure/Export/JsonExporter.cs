using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador de dados para formato JSON estruturado com streaming de baixa alocação.
/// </summary>
public static class JsonExporter
{
    public static async Task<long> ExportAsync(
        Stream outputStream,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var jsonOptions = new JsonWriterOptions
        {
            Indented = options.JsonIndented
        };

        await using var writer = new Utf8JsonWriter(outputStream, jsonOptions);
        long rowCount = 0;

        if (options.JsonStyle == JsonFormatStyle.Tabular)
        {
            writer.WriteStartObject();

            // "columns": [ ... ]
            writer.WriteStartArray("columns");
            foreach (var col in columns)
            {
                writer.WriteStringValue(col);
            }
            writer.WriteEndArray();

            // "rows": [ [ ... ], [ ... ] ]
            writer.WriteStartArray("rows");
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                writer.WriteStartArray();
                for (int i = 0; i < columns.Count; i++)
                {
                    var val = i < row.Count ? row[i] : null;
                    WriteJsonValue(writer, val);
                }
                writer.WriteEndArray();
                rowCount++;

                if (rowCount % 100 == 0)
                {
                    progress?.Report((int)rowCount);
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else
        {
            // Array of Objects: [ { "col": val }, ... ]
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                for (int i = 0; i < columns.Count; i++)
                {
                    var colName = columns[i];
                    var val = i < row.Count ? row[i] : null;
                    writer.WritePropertyName(colName);
                    WriteJsonValue(writer, val);
                }
                writer.WriteEndObject();
                rowCount++;

                if (rowCount % 100 == 0)
                {
                    progress?.Report((int)rowCount);
                }
            }
            writer.WriteEndArray();
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
        var jsonOptions = new JsonWriterOptions
        {
            Indented = options.JsonIndented
        };

        await using var writer = new Utf8JsonWriter(outputStream, jsonOptions);
        long rowCount = 0;

        int fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var values = new object[fieldCount];

        if (options.JsonStyle == JsonFormatStyle.Tabular)
        {
            writer.WriteStartObject();

            // "columns": [ ... ]
            writer.WriteStartArray("columns");
            foreach (var col in columns)
            {
                writer.WriteStringValue(col);
            }
            writer.WriteEndArray();

            // "rows": [ [ ... ], [ ... ] ]
            writer.WriteStartArray("rows");
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                reader.GetValues(values);

                writer.WriteStartArray();
                for (int i = 0; i < fieldCount; i++)
                {
                    var val = values[i] == DBNull.Value ? null : values[i];
                    WriteJsonValue(writer, val);
                }
                writer.WriteEndArray();
                rowCount++;

                if (rowCount % 100 == 0)
                {
                    progress?.Report((int)rowCount);
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else
        {
            // Array of Objects: [ { "col": val }, ... ]
            writer.WriteStartArray();
            while (await reader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                reader.GetValues(values);

                writer.WriteStartObject();
                for (int i = 0; i < fieldCount; i++)
                {
                    var colName = columns[i];
                    var val = values[i] == DBNull.Value ? null : values[i];
                    writer.WritePropertyName(colName);
                    WriteJsonValue(writer, val);
                }
                writer.WriteEndObject();
                rowCount++;

                if (rowCount % 100 == 0)
                {
                    progress?.Report((int)rowCount);
                }
            }
            writer.WriteEndArray();
        }

        await writer.FlushAsync(ct);
        progress?.Report((int)rowCount);
        return rowCount;
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? val)
    {
        if (val == null || val == DBNull.Value)
        {
            writer.WriteNullValue();
            return;
        }

        switch (val)
        {
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case byte b:
                writer.WriteNumberValue(b);
                break;
            case short s:
                writer.WriteNumberValue(s);
                break;
            case ushort us:
                writer.WriteNumberValue(us);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("yyyy-MM-ddTHH:mm:ss.fff"));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
                break;
            case byte[] bytes:
                writer.WriteStringValue(Convert.ToBase64String(bytes));
                break;
            default:
                writer.WriteStringValue(val.ToString() ?? "");
                break;
        }
    }
}
