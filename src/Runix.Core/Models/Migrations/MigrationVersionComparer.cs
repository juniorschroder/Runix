using System;
using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models.Migrations;

/// <summary>
/// Natural semantic version comparator (e.g. 1 < 2 < 10, 1.9 < 1.10, 2.0.0 < 2.0.1).
/// Avoids naive lexicographical sorting bugs where "V10" would precede "V2".
/// </summary>
public class MigrationVersionComparer : IComparer<string>
{
    public static readonly MigrationVersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        // Clean any leading 'V' or 'v'
        string cleanX = x.TrimStart('V', 'v').Trim();
        string cleanY = y.TrimStart('V', 'v').Trim();

        var partsX = cleanX.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var partsY = cleanY.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();

        int maxLen = Math.Max(partsX.Length, partsY.Length);
        for (int i = 0; i < maxLen; i++)
        {
            int vx = i < partsX.Length ? partsX[i] : 0;
            int vy = i < partsY.Length ? partsY[i] : 0;
            if (vx != vy)
            {
                return vx.CompareTo(vy);
            }
        }

        return 0;
    }
}
