using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Explain;

public partial class ExplainPlanView : UserControl
{
    private ExecutionPlanModel? _currentPlan;
    private ExplainNodeControl? _selectedNodeControl;
    private readonly List<ExplainNodeControl> _allNodeControls = new();

    public event Action<string>? CreateIndexRequested;

    public ExplainPlanView()
    {
        InitializeComponent();

        BtnCloseInspector.Click += (s, e) =>
        {
            PanelInspector.IsVisible = false;
            _selectedNodeControl?.SetSelected(false);
            _selectedNodeControl = null;
        };

        BtnCopyIndexSql.Click += async (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null && !string.IsNullOrWhiteSpace(TxtIndexSql.Text))
            {
                await topLevel.Clipboard.SetTextAsync(TxtIndexSql.Text);
                BtnCopyIndexSql.Content = "✓ Copiado!";
            }
        };

        BtnApplyIndex.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(TxtIndexSql.Text))
            {
                CreateIndexRequested?.Invoke(TxtIndexSql.Text);
            }
        };
    }

    public void LoadPlan(ExecutionPlanModel? plan)
    {
        _currentPlan = plan;
        PanelTreeGraph.Children.Clear();
        _allNodeControls.Clear();
        _selectedNodeControl = null;
        PanelInspector.IsVisible = false;
        BtnCopyIndexSql.Content = "📋 Copiar SQL";

        if (plan?.RootNode == null)
        {
            PanelEmptyState.IsVisible = true;
            PanelRecommendations.IsVisible = false;
            return;
        }

        PanelEmptyState.IsVisible = false;

        // Recommendations Banner
        if (plan.Recommendations.Count > 0)
        {
            var rec = plan.Recommendations[0];
            TxtRecommendationTitle.Text = $"💡 Otimização Recomendada para '{rec.TableName}'";
            TxtRecommendationReason.Text = rec.Reason;
            TxtIndexSql.Text = rec.CreateIndexSql;
            PanelRecommendations.IsVisible = true;
        }
        else
        {
            PanelRecommendations.IsVisible = false;
        }

        // Build Visual Top-Down Tree
        var visualRoot = BuildNodeTreeVisual(plan.RootNode);
        PanelTreeGraph.Children.Add(visualRoot);

        // Auto-select bottleneck node or root
        var allNodes = ExecutionPlanModel.FlattenNodes(plan.RootNode).ToList();
        var targetNode = allNodes.FirstOrDefault(n => n.IsBottleneck) ?? plan.RootNode;
        SelectNode(targetNode);
    }

    private Control BuildNodeTreeVisual(ExplainNode node)
    {
        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var nodeControl = new ExplainNodeControl
        {
            DataContext = node
        };

        nodeControl.NodeClicked += OnNodeClicked;
        _allNodeControls.Add(nodeControl);
        container.Children.Add(nodeControl);

        if (node.Children.Count > 0)
        {
            // Vertical connecting line from parent
            var lineDown = new Rectangle
            {
                Width = 2,
                Height = 16,
                Fill = Brush.Parse("#1e2d42"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            container.Children.Add(lineDown);

            if (node.Children.Count == 1)
            {
                container.Children.Add(BuildNodeTreeVisual(node.Children[0]));
            }
            else
            {
                // Multiple children horizontal branch
                var childrenPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 20,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                foreach (var child in node.Children)
                {
                    childrenPanel.Children.Add(BuildNodeTreeVisual(child));
                }

                container.Children.Add(childrenPanel);
            }
        }

        return container;
    }

    private void OnNodeClicked(ExplainNode node)
    {
        SelectNode(node);
    }

    public void SelectNode(ExplainNode node)
    {
        if (_selectedNodeControl != null)
        {
            _selectedNodeControl.SetSelected(false);
        }

        var matchingControl = _allNodeControls.FirstOrDefault(c => c.Node?.Id == node.Id);
        if (matchingControl != null)
        {
            matchingControl.SetSelected(true);
            _selectedNodeControl = matchingControl;
        }

        // Populate Inspector
        TxtInspectorNodeType.Text = node.NodeType;
        TxtInspectorRelation.Text = !string.IsNullOrEmpty(node.TargetDescription) ? node.TargetDescription : "-";
        TxtInspectorTotalCost.Text = $"{node.TotalCost:N1}";
        TxtInspectorCostPercent.Text = $"{node.CostPercent:F1}%";
        TxtInspectorCostPercent.Foreground = Brush.Parse(node.CostColor);
        TxtInspectorPlanRows.Text = $"{node.PlanRows:N0}";
        TxtInspectorPlanWidth.Text = $"{node.PlanWidth} bytes";

        // Timings (Analyze)
        var hasAnalyze = node.ActualTotalTimeMs.HasValue;
        CardInspectorTimings.IsVisible = hasAnalyze;
        if (hasAnalyze)
        {
            TxtInspectorActualTime.Text = $"{node.ActualTotalTimeMs!.Value:F3} ms";
            TxtInspectorActualRows.Text = $"{node.ActualRows ?? 0:N0}";
            TxtInspectorLoops.Text = $"{node.ActualLoops ?? 1:N0}";
            TxtInspectorStartupTime.Text = $"{node.ActualStartupTimeMs ?? 0:F3} ms";
        }

        // Buffers
        var hasBuffers = node.SharedHitBlocks.HasValue || node.SharedReadBlocks.HasValue;
        CardInspectorBuffers.IsVisible = hasBuffers;
        if (hasBuffers)
        {
            TxtInspectorSharedHits.Text = $"{node.SharedHitBlocks ?? 0:N0} blocos";
            TxtInspectorSharedReads.Text = $"{node.SharedReadBlocks ?? 0:N0} blocos";
        }

        // Filters
        var hasFilter = !string.IsNullOrEmpty(node.FilterCondition) || (node.RowsRemovedByFilter ?? 0) > 0;
        CardInspectorFilters.IsVisible = hasFilter;
        if (hasFilter)
        {
            TxtInspectorFilter.Text = node.FilterCondition ?? "-";
            TxtInspectorRowsRemoved.Text = $"{node.RowsRemovedByFilter ?? 0:N0} linhas";
        }

        PanelInspector.IsVisible = true;
    }
}
