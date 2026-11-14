namespace Runix.Core.Models;

public sealed class CompletionItem
{
    public required string Label { get; init; }
    public required CompletionItemKind Kind { get; init; }
    public string? Detail { get; init; }
    public string? InsertText { get; init; }
    public int Priority { get; init; }

    public string EffectiveInsertText => InsertText ?? Label;

    public string BadgeText => Kind switch
    {
        CompletionItemKind.Keyword => "[KW]",
        CompletionItemKind.Table => "[TBL]",
        CompletionItemKind.View => "[VIEW]",
        CompletionItemKind.Column => "[COL]",
        CompletionItemKind.Snippet => "[SNP]",
        _ => "[?]"
    };

    public string BadgeColor => Kind switch
    {
        CompletionItemKind.Keyword => "#38bdf8", // Blue
        CompletionItemKind.Table => "#c084fc",   // Purple
        CompletionItemKind.View => "#2dd4bf",    // Teal
        CompletionItemKind.Column => "#34d399",  // Emerald
        CompletionItemKind.Snippet => "#fb923c", // Orange
        _ => "#94a3b8"
    };

    public override string ToString() => $"{BadgeText} {Label}";
}
