using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Runix.Core.Models;

namespace Runix.Desktop.Services;

public class SqlCompletionData : ICompletionData
{
    public CompletionItem Item { get; }
    private Control? _cachedContent;

    public SqlCompletionData(CompletionItem item)
    {
        Item = item;
    }

    public IImage? Image => null;

    public string Text => Item.EffectiveInsertText;

    public object Content => _cachedContent ??= CreateContent();

    private Control CreateContent()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(4, 2)
        };

        var badgeBorder = new Border
        {
            Background = Brush.Parse("#1e2d42"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = Item.BadgeText,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse(Item.BadgeColor),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(badgeBorder, 0);
        grid.Children.Add(badgeBorder);

        var labelText = new TextBlock
        {
            Text = Item.Label,
            Foreground = Brush.Parse("#f0f6fc"),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelText, 1);
        grid.Children.Add(labelText);

        if (!string.IsNullOrEmpty(Item.Detail))
        {
            var detailText = new TextBlock
            {
                Text = Item.Detail,
                Foreground = Brush.Parse("#8b9cb3"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(detailText, 2);
            grid.Children.Add(detailText);
        }

        return grid;
    }

    public object Description => $"{Item.BadgeText}: {Item.Label}\n{Item.Detail}";

    public double Priority => Item.Kind switch
    {
        CompletionItemKind.Table => 10,
        CompletionItemKind.Column => 20,
        CompletionItemKind.Keyword => 5,
        CompletionItemKind.Snippet => 8,
        _ => Item.Priority
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment.Offset, completionSegment.Length, Item.EffectiveInsertText);
    }
}
