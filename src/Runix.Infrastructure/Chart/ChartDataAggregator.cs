using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Runix.Core.Models;
using Runix.Core.Models.Chart;

namespace Runix.Infrastructure.Chart;

/// <summary>
/// Motor de agregação em memória para transformar linhas de QueryResult em séries estruturadas de gráficos.
/// </summary>
public static class ChartDataAggregator
{
    private static readonly string[] RunixPalette = new[]
    {
        "#38bdf8", // Sky Blue
        "#34d399", // Emerald Green
        "#fbbf24", // Amber Gold
        "#f87171", // Coral Red
        "#a78bfa", // Lavender
        "#f472b6", // Rose Pink
        "#22d3ee", // Cyan
        "#4ade80", // Light Green
        "#e879f9", // Magenta
        "#facc15"  // Yellow
    };

    public static ChartSeriesData Aggregate(QueryResult queryResult, ChartConfiguration config)
    {
        var result = new ChartSeriesData
        {
            SeriesName = string.IsNullOrEmpty(config.Title) ? config.YColumn : config.Title,
            XAxisLabel = config.XColumn,
            YAxisLabel = config.YColumn
        };

        if (queryResult.Rows.Count == 0 || queryResult.Columns.Count == 0)
        {
            return result;
        }

        // 1. Locate column indices
        var xColName = string.IsNullOrEmpty(config.XColumn) ? queryResult.Columns[0] : config.XColumn;
        var xIdx = queryResult.Columns.FindIndex(c => string.Equals(c, xColName, StringComparison.OrdinalIgnoreCase));
        if (xIdx < 0) xIdx = 0;
        result.XAxisLabel = queryResult.Columns[xIdx];

        var yColName = config.YColumn;
        var yIdx = -1;
        if (!string.IsNullOrEmpty(yColName))
        {
            yIdx = queryResult.Columns.FindIndex(c => string.Equals(c, yColName, StringComparison.OrdinalIgnoreCase));
        }

        if (yIdx < 0)
        {
            // Auto-detect first numeric column or fallback
            yIdx = DetectFirstNumericColumn(queryResult, xIdx);
            if (yIdx >= 0)
            {
                result.YAxisLabel = queryResult.Columns[yIdx];
            }
            else
            {
                yIdx = queryResult.Columns.Count > 1 && xIdx == 0 ? 1 : 0;
                result.YAxisLabel = queryResult.Columns[yIdx];
            }
        }

        // 2. Perform aggregation or raw extraction
        List<ChartDataPoint> points;
        if (config.Aggregation == AggregationType.None)
        {
            points = ExtractRawPoints(queryResult, xIdx, yIdx);
        }
        else
        {
            points = AggregatePoints(queryResult, xIdx, yIdx, config.Aggregation);
        }

        // 3. Apply Top N filtering if configured
        if (config.TopN.HasValue && config.TopN.Value > 0 && points.Count > config.TopN.Value)
        {
            points = ApplyTopN(points, config.TopN.Value, config.Aggregation);
        }

        // 4. Calculate totals, percentages, and assign palette colors
        var totalSum = points.Sum(p => Math.Max(0, p.Value));
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            p.ColorHex = RunixPalette[i % RunixPalette.Length];
            p.Percentage = totalSum > 0 ? (p.Value / totalSum) * 100.0 : 0.0;
            p.FormattedValue = FormatValue(p.Value);
        }

        result.Points = points;
        return result;
    }

    private static List<ChartDataPoint> ExtractRawPoints(QueryResult queryResult, int xIdx, int yIdx)
    {
        var list = new List<ChartDataPoint>();
        for (int i = 0; i < queryResult.Rows.Count; i++)
        {
            var row = queryResult.Rows[i];
            var category = row.Count > xIdx && row[xIdx] != null ? row[xIdx]!.ToString() ?? string.Empty : $"Linha {i + 1}";
            var rawVal = row.Count > yIdx ? row[yIdx] : null;
            var val = ParseDouble(rawVal);

            list.Add(new ChartDataPoint
            {
                Category = category,
                Value = val,
                XSecondary = i
            });
        }
        return list;
    }

    private static List<ChartDataPoint> AggregatePoints(QueryResult queryResult, int xIdx, int yIdx, AggregationType aggregation)
    {
        var groups = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in queryResult.Rows)
        {
            var category = row.Count > xIdx && row[xIdx] != null ? row[xIdx]!.ToString() ?? string.Empty : "Nulo";
            var rawVal = row.Count > yIdx ? row[yIdx] : null;
            var val = ParseDouble(rawVal);

            if (!groups.TryGetValue(category, out var valList))
            {
                valList = new List<double>();
                groups[category] = valList;
            }
            valList.Add(val);
        }

        var list = new List<ChartDataPoint>();
        foreach (var kvp in groups)
        {
            var category = kvp.Key;
            var values = kvp.Value;
            double aggVal = 0;

            switch (aggregation)
            {
                case AggregationType.Sum:
                    aggVal = values.Sum();
                    break;
                case AggregationType.Average:
                    aggVal = values.Count > 0 ? values.Average() : 0;
                    break;
                case AggregationType.Count:
                    aggVal = values.Count;
                    break;
                case AggregationType.Min:
                    aggVal = values.Count > 0 ? values.Min() : 0;
                    break;
                case AggregationType.Max:
                    aggVal = values.Count > 0 ? values.Max() : 0;
                    break;
            }

            list.Add(new ChartDataPoint
            {
                Category = category,
                Value = aggVal
            });
        }

        return list;
    }

    private static List<ChartDataPoint> ApplyTopN(List<ChartDataPoint> points, int topN, AggregationType agg)
    {
        var sorted = points.OrderByDescending(p => p.Value).ToList();
        var top = sorted.Take(topN).ToList();
        var remainder = sorted.Skip(topN).ToList();

        if (remainder.Count > 0)
        {
            double otherValue = agg switch
            {
                AggregationType.Average => remainder.Average(p => p.Value),
                AggregationType.Min => remainder.Min(p => p.Value),
                AggregationType.Max => remainder.Max(p => p.Value),
                _ => remainder.Sum(p => p.Value)
            };

            top.Add(new ChartDataPoint
            {
                Category = "Outros",
                Value = otherValue
            });
        }

        return top;
    }

    public static int DetectFirstNumericColumn(QueryResult queryResult, int excludeIdx = -1)
    {
        for (int col = 0; col < queryResult.Columns.Count; col++)
        {
            if (col == excludeIdx) continue;

            // Sample first few rows to check if numeric
            int numericHits = 0;
            int totalSamples = Math.Min(queryResult.Rows.Count, 10);

            for (int r = 0; r < totalSamples; r++)
            {
                var row = queryResult.Rows[r];
                if (col < row.Count && IsNumericType(row[col]))
                {
                    numericHits++;
                }
            }

            if (totalSamples > 0 && numericHits >= (totalSamples / 2))
            {
                return col;
            }
        }

        return -1;
    }

    private static bool IsNumericType(object? val)
    {
        if (val == null) return false;
        return val is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            || (val is string str && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
    }

    private static double ParseDouble(object? val)
    {
        if (val == null || val == DBNull.Value) return 0.0;

        if (val is double d) return d;
        if (val is float f) return f;
        if (val is int i) return i;
        if (val is long l) return l;
        if (val is decimal dec) return (double)dec;
        if (val is short s) return s;
        if (val is byte b) return b;

        var str = val.ToString();
        if (string.IsNullOrWhiteSpace(str)) return 0.0;

        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        if (double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
            return parsed;

        return 0.0;
    }

    private static string FormatValue(double val)
    {
        if (Math.Abs(val % 1) < 0.0001)
        {
            return val.ToString("N0", CultureInfo.InvariantCulture);
        }
        return val.ToString("N2", CultureInfo.InvariantCulture);
    }
}
