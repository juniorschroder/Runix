using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MiniExcelLibs;
using Runix.Core.Models;

namespace Runix.Infrastructure.Export;

/// <summary>
/// Exportador de dados para formato nativo Excel OpenXML (.xlsx) utilizando MiniExcel.
/// </summary>
public static class ExcelExporter
{
    public static async Task<long> ExportAsync(
        Stream outputStream,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        ExportOptions options,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        long rowCount = 0;
        var sheetName = string.IsNullOrWhiteSpace(options.SheetName) ? "Data" : options.SheetName;

        var dictRows = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var val = i < row.Count ? row[i] : null;
                dict[col] = val == DBNull.Value ? null : val;
            }
            dictRows.Add(dict);
            rowCount++;

            if (rowCount % 100 == 0)
            {
                progress?.Report((int)rowCount);
            }
        }

        await MiniExcel.SaveAsAsync(
            outputStream,
            dictRows,
            sheetName: sheetName,
            cancellationToken: ct);

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
        long rowCount = 0;
        var sheetName = string.IsNullOrWhiteSpace(options.SheetName) ? "Data" : options.SheetName;
        int fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var values = new object[fieldCount];
        var dictRows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            reader.GetValues(values);

            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < fieldCount; i++)
            {
                var val = values[i] == DBNull.Value ? null : values[i];
                dict[columns[i]] = val;
            }

            dictRows.Add(dict);
            rowCount++;

            if (rowCount % 100 == 0)
            {
                progress?.Report((int)rowCount);
            }
        }

        await MiniExcel.SaveAsAsync(
            outputStream,
            dictRows,
            sheetName: sheetName,
            cancellationToken: ct);

        progress?.Report((int)rowCount);
        return rowCount;
    }
}
