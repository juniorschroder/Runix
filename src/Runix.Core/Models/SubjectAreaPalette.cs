using System;
using System.Collections.Generic;
using System.Linq;

namespace Runix.Core.Models;

/// <summary>
/// Paleta de 8 cores predefinidas alinhadas ao design system RUNIX Dark para Subject Areas do DER.
/// </summary>
public static class SubjectAreaPalette
{
    public const string Blue = "#008bb9";
    public const string Purple = "#a855f7";
    public const string Emerald = "#10b981";
    public const string Amber = "#f59e0b";
    public const string Pink = "#ec4899";
    public const string Indigo = "#6366f1";
    public const string Cyan = "#06b6d4";
    public const string Orange = "#f97316";

    public static readonly IReadOnlyList<string> AllColors = new[]
    {
        Blue,
        Purple,
        Emerald,
        Amber,
        Pink,
        Indigo,
        Cyan,
        Orange
    };

    public static string DefaultColor => Blue;

    public static bool IsValidColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        return AllColors.Any(c => string.Equals(c, hex.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string GetColorName(string hex)
    {
        return hex?.ToLowerInvariant() switch
        {
            "#008bb9" => "Azul RUNIX",
            "#a855f7" => "Roxo",
            "#10b981" => "Esmeralda",
            "#f59e0b" => "Âmbar",
            "#ec4899" => "Rosa",
            "#6366f1" => "Índigo",
            "#06b6d4" => "Ciano",
            "#f97316" => "Laranja",
            _ => "Personalizado"
        };
    }
}
