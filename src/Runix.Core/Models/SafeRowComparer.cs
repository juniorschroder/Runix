namespace Runix.Core.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Comparador resiliente que previne falhas de tipo heterogêneo
/// como ArgumentException ("Object must be of type String") ao ordenar colunas de dados com tipos mistos (FKs, IDs numéricos, strings, nulls).
/// </summary>
public class SafeRowComparer : IComparer
{
    public static readonly SafeRowComparer Instance = new();
    private readonly int _columnIndex;

    public SafeRowComparer(int columnIndex = -1)
    {
        _columnIndex = columnIndex;
    }

    public int Compare(object? x, object? y)
    {
        var valX = ExtractValue(x, _columnIndex);
        var valY = ExtractValue(y, _columnIndex);

        return CompareValues(valX, valY);
    }

    public static object? ExtractValue(object? item, int colIndex)
    {
        if (item is null || item == DBNull.Value) return null;

        if (colIndex >= 0)
        {
            if (item is IReadOnlyList<object?> readOnlyList)
            {
                return (colIndex < readOnlyList.Count) ? readOnlyList[colIndex] : null;
            }
            if (item is IList list)
            {
                return (colIndex < list.Count) ? list[colIndex] : null;
            }
        }

        return item;
    }

    public static int CompareValues(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null || x == DBNull.Value) return (y is null || y == DBNull.Value) ? 0 : -1;
        if (y is null || y == DBNull.Value) return 1;

        // Se ambos forem strings
        if (x is string s1 && y is string s2)
        {
            return string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
        }

        // Se ambos forem exatamente do mesmo tipo e implementarem IComparable
        if (x.GetType() == y.GetType() && x is IComparable comp)
        {
            try
            {
                return comp.CompareTo(y);
            }
            catch
            {
                // Fallback para comparação segura
            }
        }

        // Se forem ambos tipos numéricos nativos
        if (IsNumeric(x) && IsNumeric(y))
        {
            try
            {
                var numX = Convert.ToDouble(x, CultureInfo.InvariantCulture);
                var numY = Convert.ToDouble(y, CultureInfo.InvariantCulture);
                return numX.CompareTo(numY);
            }
            catch
            {
                // Fallback
            }
        }

        var strX = x.ToString() ?? "";
        var strY = y.ToString() ?? "";

        // Tentar comparação numérica entre número e string ou strings numéricas (ex: "42" vs 42L)
        if (double.TryParse(strX, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedX) &&
            double.TryParse(strY, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedY))
        {
            return parsedX.CompareTo(parsedY);
        }

        // Fallback para comparação textual segura case-insensitive
        return string.Compare(strX, strY, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumeric(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }
}
