using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Infrastructure.Import;

/// <summary>
/// Parser para arquivos JSON nos formatos Array de Objetos ou Formato Tabular.
/// </summary>
public static class JsonDataParser
{
    public static async Task<ImportPreviewData> PreviewAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var headers = new List<string>();
        var previewRows = new List<IReadOnlyList<object?>>();
        long estimatedTotal = 0;

        if (root.ValueKind == JsonValueKind.Array)
        {
            var firstItem = root.EnumerateArray().FirstOrDefault();
            if (firstItem.ValueKind == JsonValueKind.Array)
            {
                headers = firstItem.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
                estimatedTotal = Math.Max(0, root.GetArrayLength() - 1);

                int count = 0;
                foreach (var item in root.EnumerateArray().Skip(1))
                {
                    if (item.ValueKind == JsonValueKind.Array)
                    {
                        var rowValues = item.EnumerateArray().Select(ExtractJsonValue).ToList();
                        previewRows.Add(rowValues);
                        count++;
                        if (count >= 10) break;
                    }
                }
            }
            else
            {
                estimatedTotal = root.GetArrayLength();

                // Extract headers from first non-null object
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        headers = item.EnumerateObject().Select(p => p.Name).ToList();
                        break;
                    }
                }

                int count = 0;
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var rowValues = new object?[headers.Count];
                        for (int i = 0; i < headers.Count; i++)
                        {
                            if (item.TryGetProperty(headers[i], out var prop))
                            {
                                rowValues[i] = ExtractJsonValue(prop);
                            }
                        }
                        previewRows.Add(rowValues);
                        count++;
                        if (count >= 10) break;
                    }
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // Check tabular format { "columns": [...], "rows": [...] }
            if (root.TryGetProperty("columns", out var colsElem) && colsElem.ValueKind == JsonValueKind.Array)
            {
                headers = colsElem.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
            }

            if (root.TryGetProperty("rows", out var rowsElem) && rowsElem.ValueKind == JsonValueKind.Array)
            {
                estimatedTotal = rowsElem.GetArrayLength();
                int count = 0;
                foreach (var rowItem in rowsElem.EnumerateArray())
                {
                    if (rowItem.ValueKind == JsonValueKind.Array)
                    {
                        var rowValues = rowItem.EnumerateArray().Select(ExtractJsonValue).ToList();
                        previewRows.Add(rowValues);
                        count++;
                        if (count >= 10) break;
                    }
                }
            }
        }

        return new ImportPreviewData
        {
            DetectedFormat = ImportFileFormat.Json,
            DetectedEncoding = "UTF-8",
            Headers = headers,
            PreviewRows = previewRows,
            EstimatedTotalRows = estimatedTotal
        };
    }

    public static async IAsyncEnumerable<IReadOnlyList<object?>> ReadAllRowsAsync(
        string filePath,
        IReadOnlyList<string> headers,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray().Skip(1))
                {
                    ct.ThrowIfCancellationRequested();
                    if (item.ValueKind == JsonValueKind.Array)
                    {
                        var rowValues = item.EnumerateArray().Select(ExtractJsonValue).ToList();
                        yield return rowValues;
                    }
                }
            }
            else
            {
                foreach (var item in root.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var rowValues = new object?[headers.Count];
                        for (int i = 0; i < headers.Count; i++)
                        {
                            if (item.TryGetProperty(headers[i], out var prop))
                            {
                                rowValues[i] = ExtractJsonValue(prop);
                            }
                        }
                        yield return rowValues;
                    }
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("rows", out var rowsElem) &&
                 rowsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var rowItem in rowsElem.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (rowItem.ValueKind == JsonValueKind.Array)
                {
                    var rowValues = rowItem.EnumerateArray().Select(ExtractJsonValue).ToList();
                    yield return rowValues;
                }
            }
        }
    }

    public static object? ExtractJsonValue(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => elem.TryGetInt64(out var l) ? l : elem.GetDouble(),
        JsonValueKind.String => elem.GetString(),
        _ => elem.GetRawText()
    };
}
