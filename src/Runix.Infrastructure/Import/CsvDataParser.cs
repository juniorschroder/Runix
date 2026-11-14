using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Infrastructure.Import;

/// <summary>
/// Parser para arquivos CSV / TSV / Delimitados com detecção automática de separador.
/// </summary>
public static class CsvDataParser
{
    private static readonly char[] DelimiterCandidates = { ',', ';', '\t', '|' };

    public static async Task<ImportPreviewData> PreviewAsync(
        string filePath,
        char? explicitDelimiter = null,
        CancellationToken ct = default)
    {
        var encoding = DetectEncoding(filePath);
        using var reader = new StreamReader(filePath, encoding);

        var sampleLines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null && sampleLines.Count < 20)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                sampleLines.Add(line);
            }
        }

        if (sampleLines.Count == 0)
        {
            return new ImportPreviewData
            {
                DetectedFormat = ImportFileFormat.Csv,
                DetectedEncoding = encoding.EncodingName
            };
        }

        var delimiter = explicitDelimiter ?? DetectDelimiter(sampleLines);

        // Parse sample lines into tokens
        var parsedSample = sampleLines.Select(l => ParseCsvLine(l, delimiter)).ToList();

        var headers = parsedSample.Count > 0 ? parsedSample[0] : new List<string>();
        var dataRows = parsedSample.Skip(1).Take(10).Cast<IReadOnlyList<object?>>().ToList();

        // Estimate total lines
        long estimatedTotal = EstimateTotalLines(filePath, sampleLines);

        return new ImportPreviewData
        {
            DetectedFormat = ImportFileFormat.Csv,
            DetectedDelimiter = delimiter,
            DetectedEncoding = encoding.EncodingName,
            Headers = headers,
            PreviewRows = dataRows,
            EstimatedTotalRows = Math.Max(dataRows.Count, estimatedTotal - 1)
        };
    }

    public static async IAsyncEnumerable<IReadOnlyList<string>> ReadAllRowsAsync(
        string filePath,
        char delimiter,
        bool hasHeader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var encoding = DetectEncoding(filePath);
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536, useAsync: true);
        using var reader = new StreamReader(stream, encoding);

        bool isFirst = true;
        string? line;
        var multiLineBuilder = new StringBuilder();
        bool inQuotes = false;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();

            if (isFirst && hasHeader)
            {
                isFirst = false;
                continue;
            }
            isFirst = false;

            if (inQuotes)
            {
                multiLineBuilder.AppendLine().Append(line);
            }
            else
            {
                multiLineBuilder.Clear().Append(line);
            }

            // Check quote balance
            inQuotes = HasUnbalancedQuotes(multiLineBuilder.ToString());
            if (!inQuotes)
            {
                var row = ParseCsvLine(multiLineBuilder.ToString(), delimiter);
                yield return row;
                multiLineBuilder.Clear();
            }
        }

        if (multiLineBuilder.Length > 0)
        {
            yield return ParseCsvLine(multiLineBuilder.ToString(), delimiter);
        }
    }

    public static char DetectDelimiter(IReadOnlyList<string> sampleLines)
    {
        var bestDelimiter = ',';
        double lowestVariance = double.MaxValue;
        int maxAverage = 0;

        foreach (var candidate in DelimiterCandidates)
        {
            var counts = sampleLines.Select(l => CountDelimiterOccurrences(l, candidate)).ToList();
            if (counts.All(c => c == 0)) continue;

            double avg = counts.Average();
            double variance = counts.Select(c => Math.Pow(c - avg, 2)).Average();

            if (avg > 0 && variance < lowestVariance)
            {
                lowestVariance = variance;
                bestDelimiter = candidate;
                maxAverage = (int)avg;
            }
            else if (Math.Abs(variance - lowestVariance) < 0.001 && avg > maxAverage)
            {
                bestDelimiter = candidate;
                maxAverage = (int)avg;
            }
        }

        return bestDelimiter;
    }

    private static int CountDelimiterOccurrences(string line, char delimiter)
    {
        int count = 0;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }

    private static bool HasUnbalancedQuotes(string text)
    {
        int quoteCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') quoteCount++;
        }
        return (quoteCount % 2) != 0;
    }

    public static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    public static Encoding DetectEncoding(string filePath)
    {
        byte[] bom = new byte[4];
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int read = file.Read(bom, 0, 4);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            {
                return Encoding.UTF8;
            }
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            {
                return Encoding.Unicode;
            }
        }
        return Encoding.UTF8;
    }

    private static long EstimateTotalLines(string filePath, IReadOnlyList<string> sampleLines)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (sampleLines.Count == 0) return 0;

            long totalSampleBytes = sampleLines.Sum(l => l.Length + 1);
            long avgBytesPerLine = Math.Max(1, totalSampleBytes / sampleLines.Count);
            return Math.Max(sampleLines.Count, info.Length / avgBytesPerLine);
        }
        catch
        {
            return sampleLines.Count;
        }
    }
}
