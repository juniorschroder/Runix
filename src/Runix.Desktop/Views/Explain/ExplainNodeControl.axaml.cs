using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Explain;

public partial class ExplainNodeControl : UserControl
{
    public ExplainNode? Node => DataContext as ExplainNode;

    public event Action<ExplainNode>? NodeClicked;

    private bool _isSelected;

    public ExplainNodeControl()
    {
        InitializeComponent();

        PointerPressed += (s, e) =>
        {
            if (Node != null)
            {
                NodeClicked?.Invoke(Node);
            }
        };

        DataContextChanged += (s, e) => UpdateStyling();
    }

    public void SetSelected(bool isSelected)
    {
        _isSelected = isSelected;
        UpdateStyling();
    }

    private void UpdateStyling()
    {
        if (Node == null) return;

        // Operator Icon mapping
        TxtOperatorIcon.Text = Node.OperatorCategory switch
        {
            "Scan" => Node.NodeType.Contains("Index", StringComparison.OrdinalIgnoreCase) ? "🔍" : "🗄",
            "Index Scan" => "🔍",
            "Join" => "🔗",
            "Sort" => "🔃",
            "Aggregate" => "Σ",
            "Filter" => "⚙",
            _ => "⚡"
        };

        var costBrush = Brush.Parse(Node.CostColor);
        TxtCostPercent.Foreground = costBrush;

        if (_isSelected)
        {
            NodeBorder.BorderBrush = Brush.Parse("#38bdf8");
            NodeBorder.BorderThickness = new Avalonia.Thickness(2.5);
            NodeBorder.Background = Brush.Parse("#1a2638");
        }
        else
        {
            NodeBorder.BorderBrush = costBrush;
            NodeBorder.BorderThickness = new Avalonia.Thickness(1.5);
            NodeBorder.Background = Brush.Parse("#141d2b");
        }
    }
}
