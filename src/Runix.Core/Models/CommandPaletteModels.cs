using System;

namespace Runix.Core.Models;

public enum CommandPaletteCategory
{
    Action,
    Table,
    View,
    ProgrammableObject,
    SavedQuery,
    Snippet
}

public sealed class CommandPaletteItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public CommandPaletteCategory Category { get; init; }
    public string Icon { get; init; } = "⚡";
    public string ShortcutHint { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public bool IsFavorite { get; init; }

    public string BadgeText => Category switch
    {
        CommandPaletteCategory.Action => "ACTION",
        CommandPaletteCategory.Table => "TABLE",
        CommandPaletteCategory.View => "VIEW",
        CommandPaletteCategory.ProgrammableObject => "DDL",
        CommandPaletteCategory.SavedQuery => "QUERY",
        CommandPaletteCategory.Snippet => "SNIPPET",
        _ => "ITEM"
    };

    public string BadgeBackground => Category switch
    {
        CommandPaletteCategory.Action => "#1e3a5f",
        CommandPaletteCategory.Table => "#123524",
        CommandPaletteCategory.View => "#2e1f47",
        CommandPaletteCategory.ProgrammableObject => "#3b2314",
        CommandPaletteCategory.SavedQuery => "#3b2f14",
        CommandPaletteCategory.Snippet => "#1f2937",
        _ => "#141d2b"
    };

    public string BadgeForeground => Category switch
    {
        CommandPaletteCategory.Action => "#38bdf8",
        CommandPaletteCategory.Table => "#34d399",
        CommandPaletteCategory.View => "#c084fc",
        CommandPaletteCategory.ProgrammableObject => "#fb923c",
        CommandPaletteCategory.SavedQuery => "#fbbf24",
        CommandPaletteCategory.Snippet => "#94a3b8",
        _ => "#8b9cb3"
    };

    public bool HasShortcut => !string.IsNullOrWhiteSpace(ShortcutHint);
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
}

public sealed record CommandPaletteScoredItem(CommandPaletteItem Item, int Score);
