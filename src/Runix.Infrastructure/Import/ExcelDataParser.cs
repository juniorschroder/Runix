using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiniExcelLibs;
using Runix.Core.Models;

namespace Runix.Infrastructure.Import;

/// <summary>
/// Parser para planilhas Excel OpenXML (.xlsx) utilizando MiniExcel.
/// </summary>
public static class ExcelDataParser
{
    public static Task<ImportPreviewData> PreviewAsync(string filePath, CancellationToken ct = default)
    {
        var headers = new List<string>();
        var previewRows = new List<IReadOnlyList<object?>>();
        long estimatedTotal = 0;

        // Query returns rows as IDictionary<string, object> or dynamic
        var allRows = MiniExcel.Query(filePath, useHeaderRow: false).ToList();
        estimatedTotal = Math.Max(0, allRows.Count - 1);

        if (allRows.Count > 0)
        {
            var headerRow = allRows[0] as IDictionary<string, object?>;
            if (headerRow != null)
            {
                headers = headerRow.Values.Select(v => v?.ToString() ?? "").ToList();
            }

            foreach (var row in allRows.Skip(1).Take(10))
            {
                var rowDict = row as IDictionary<string, object?>;
                if (rowDict != null)
                {
                    previewRows.Add(rowDict.Values.ToList());
                }
            }
        }

        var result = new ImportPreviewData
        {
            DetectedFormat = ImportFileFormat.Excel,
            DetectedEncoding = "Binary OpenXML",
            Headers = headers,
            PreviewRows = previewRows,
            EstimatedTotalRows = estimatedTotal
        };

        return Task.FromResult(result);
    }

    public static async IAsyncEnumerable<IReadOnlyList<object?>> ReadAllRowsAsync(
        string filePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var rows = await Task.Run(() => MiniExcel.Query(filePath, useHeaderRow: false).ToList(), ct);

        // Skip header row
        foreach (var row in rows.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            var rowDict = row as IDictionary<string, object?>;
            if (rowDict != null)
            {
                yield return rowDict.Values.ToList();
            }
        }
    }
}
