using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Runix.Core.Models;

namespace Runix.Core.Services;

public static class CellValueFormatter
{
    public static List<InspectedCellField> BuildInspectedFields(
        IReadOnlyList<string> columns,
        IReadOnlyList<object?> values)
    {
        var list = new List<InspectedCellField>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            object? val = i < values.Count ? values[i] : null;
            list.Add(InspectCell(i, columns[i], val));
        }
        return list;
    }

    public static InspectedCellField InspectCell(int columnIndex, string columnName, object? rawValue)
    {
        if (rawValue is null || rawValue is DBNull || (rawValue is string sNull && sNull == "NULL"))
        {
            return new InspectedCellField
            {
                ColumnIndex = columnIndex,
                ColumnName = columnName,
                RawValue = null,
                DisplayPreview = "NULL",
                FormattedDetail = "-- Valor Nulo (NULL) --",
                Category = CellValueCategory.Null,
                ByteOrCharLength = 0
            };
        }

        if (rawValue is byte[] bytes)
        {
            return new InspectedCellField
            {
                ColumnIndex = columnIndex,
                ColumnName = columnName,
                RawValue = bytes,
                DisplayPreview = $"[BLOB {bytes.Length:N0} bytes]",
                FormattedDetail = FormatHexDump(bytes),
                Category = CellValueCategory.HexBlob,
                ByteOrCharLength = bytes.Length
            };
        }

        string text = rawValue.ToString() ?? string.Empty;

        if (LooksLikeJson(text) && TryFormatJsonPretty(text, out string prettyJson, out _))
        {
            return new InspectedCellField
            {
                ColumnIndex = columnIndex,
                ColumnName = columnName,
                RawValue = text,
                DisplayPreview = TruncateSingleLine(text, 80),
                FormattedDetail = prettyJson,
                Category = CellValueCategory.Json,
                ByteOrCharLength = text.Length
            };
        }

        if (text.Length > 80 || text.Contains('\n') || text.Contains('\r'))
        {
            return new InspectedCellField
            {
                ColumnIndex = columnIndex,
                ColumnName = columnName,
                RawValue = text,
                DisplayPreview = TruncateSingleLine(text, 80),
                FormattedDetail = text,
                Category = CellValueCategory.LongText,
                ByteOrCharLength = text.Length
            };
        }

        return new InspectedCellField
        {
            ColumnIndex = columnIndex,
            ColumnName = columnName,
            RawValue = rawValue,
            DisplayPreview = text,
            FormattedDetail = text,
            Category = CellValueCategory.Scalar,
            ByteOrCharLength = text.Length
        };
    }

    public static bool LooksLikeJson(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = input.AsSpan().Trim();
        return (trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
               (trimmed.StartsWith("[") && trimmed.EndsWith("]"));
    }

    public static bool TryFormatJsonPretty(string? rawJson, out string formatted, out string? errorMessage)
    {
        formatted = rawJson ?? string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "JSON vazio.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryFormatJsonMinified(string? rawJson, out string minified, out string? errorMessage)
    {
        minified = rawJson ?? string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorMessage = "JSON vazio.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            minified = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            return true;
        }
        catch (JsonException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static string FormatHexDump(byte[] bytes, int maxBytes = 1024)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return "-- BLOB vazio (0 bytes) --";
        }

        int len = Math.Min(bytes.Length, maxBytes);
        var sb = new StringBuilder();
        sb.AppendLine($"Tamanho Total: {bytes.Length:N0} bytes");
        sb.AppendLine("Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII");
        sb.AppendLine("--------  -----------------------------------------------   ----------------");

        for (int offset = 0; offset < len; offset += 16)
        {
            sb.Append($"{offset:X8}  ");

            for (int i = 0; i < 16; i++)
            {
                if (offset + i < len)
                {
                    sb.Append($"{bytes[offset + i]:X2} ");
                }
                else
                {
                    sb.Append("   ");
                }

                if (i == 7)
                {
                    sb.Append(' ');
                }
            }

            sb.Append("  ");
            for (int i = 0; i < 16 && offset + i < len; i++)
            {
                byte b = bytes[offset + i];
                sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }

            sb.AppendLine();
        }

        if (bytes.Length > maxBytes)
        {
            sb.AppendLine($"... (+{bytes.Length - maxBytes:N0} bytes omitidos na pré-visualização)");
        }

        return sb.ToString().TrimEnd();
    }

    private static string TruncateSingleLine(string value, int maxLen)
    {
        string single = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= maxLen ? single : single[..maxLen] + "…";
    }
}
