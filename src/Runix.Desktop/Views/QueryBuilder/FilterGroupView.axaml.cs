using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Views.QueryBuilder;

public partial class FilterGroupView : UserControl
{
    private static readonly string[] Operators =
    {
        "=", "<>", ">", "<", ">=", "<=",
        "LIKE", "NOT LIKE", "ILIKE",
        "IN", "NOT IN",
        "BETWEEN", "NOT BETWEEN",
        "IS NULL", "IS NOT NULL",
        "CONTAINS", "STARTS WITH", "ENDS WITH"
    };

    private static readonly string[] DepthBorderColors =
    {
        "#38bdf8", // Depth 0: Sky Blue
        "#a78bfa", // Depth 1: Lavender / Purple
        "#34d399", // Depth 2: Emerald Green
        "#fbbf24"  // Depth 3+: Amber
    };

    private VisualFilterGroup? _group;
    private IReadOnlyList<string> _availableColumns = Array.Empty<string>();
    private int _depth;
    private bool _isRoot;

    public event Action? FilterChanged;
    public event Action<FilterGroupView>? RemoveRequested;

    public VisualFilterGroup? Group => _group;

    public FilterGroupView()
    {
        InitializeComponent();
        SetupHeaderEvents();
    }

    public void Configure(
        VisualFilterGroup group,
        IReadOnlyList<string> availableColumns,
        int depth = 0,
        bool isRoot = false)
    {
        _group = group;
        _availableColumns = availableColumns;
        _depth = depth;
        _isRoot = isRoot;

        // Styling based on depth
        string borderColor = DepthBorderColors[Math.Min(depth, DepthBorderColors.Length - 1)];
        BorderGroupContainer.BorderBrush = Brush.Parse(borderColor);

        BtnRemoveGroup.IsVisible = !_isRoot;

        UpdateHeaderState();
        RenderAll();
    }

    public void UpdateColumns(IReadOnlyList<string> availableColumns)
    {
        _availableColumns = availableColumns;
        RenderAll();
    }

    private void SetupHeaderEvents()
    {
        BtnToggleOperator.Click += (s, e) =>
        {
            if (_group == null) return;
            _group.LogicalOperator = _group.LogicalOperator.Equals("AND", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";
            UpdateHeaderState();
            FilterChanged?.Invoke();
        };

        ToggleNot.IsCheckedChanged += (s, e) =>
        {
            if (_group == null) return;
            _group.IsNegated = ToggleNot.IsChecked == true;
            UpdateHeaderState();
            FilterChanged?.Invoke();
        };

        BtnAddCondition.Click += (s, e) =>
        {
            if (_group == null) return;
            string defaultCol = _availableColumns.FirstOrDefault() ?? "id";
            var (alias, colName) = SplitQualifiedColumn(defaultCol);

            var cond = new VisualFilterCondition
            {
                LogicalOperator = _group.LogicalOperator,
                TableAlias = alias,
                ColumnName = colName,
                ComparisonOperator = "=",
                Value = string.Empty
            };

            _group.AddCondition(cond);
            RenderConditionRow(cond);
            UpdateRuleCounter();
            FilterChanged?.Invoke();
        };

        BtnAddSubGroup.Click += (s, e) =>
        {
            if (_group == null) return;
            var subGroup = new VisualFilterGroup
            {
                LogicalOperator = _group.LogicalOperator.Equals("AND", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND"
            };

            // Add one default condition to new subgroup
            string defaultCol = _availableColumns.FirstOrDefault() ?? "id";
            var (alias, colName) = SplitQualifiedColumn(defaultCol);
            subGroup.AddCondition(new VisualFilterCondition
            {
                TableAlias = alias,
                ColumnName = colName,
                ComparisonOperator = "=",
                Value = string.Empty
            });

            _group.AddGroup(subGroup);
            RenderSubGroupView(subGroup);
            UpdateRuleCounter();
            FilterChanged?.Invoke();
        };

        BtnRemoveGroup.Click += (s, e) => RemoveRequested?.Invoke(this);
    }

    private void UpdateHeaderState()
    {
        if (_group == null) return;

        bool isAnd = _group.LogicalOperator.Equals("AND", StringComparison.OrdinalIgnoreCase);
        BtnToggleOperator.Content = isAnd ? "AND" : "OR";
        BtnToggleOperator.Background = isAnd ? Brush.Parse("#008bb9") : Brush.Parse("#eab308");

        ToggleNot.IsChecked = _group.IsNegated;
        ToggleNot.Foreground = _group.IsNegated ? Brush.Parse("#f87171") : Brush.Parse("#8b9cb3");

        UpdateRuleCounter();
    }

    private void UpdateRuleCounter()
    {
        if (_group == null) return;
        int count = _group.Children.Count;
        TxtRuleCountBadge.Text = count == 1 ? "1 regra" : $"{count} regras";
    }

    private void RenderAll()
    {
        PanelConditions.Children.Clear();
        PanelSubGroups.Children.Clear();

        if (_group == null) return;

        foreach (var child in _group.Children.ToList())
        {
            if (child is VisualFilterCondition cond)
            {
                RenderConditionRow(cond);
            }
            else if (child is VisualFilterGroup sub)
            {
                RenderSubGroupView(sub);
            }
        }

        UpdateRuleCounter();
    }

    private void RenderConditionRow(VisualFilterCondition cond)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,115,*,Auto,Auto"),
            Margin = new Avalonia.Thickness(0, 1)
        };

        // 1. Column Selector
        var cmbColumn = new ComboBox
        {
            ItemsSource = _availableColumns,
            FontSize = 10.5,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        string fullQualified = string.IsNullOrWhiteSpace(cond.TableAlias)
            ? cond.ColumnName
            : $"{cond.TableAlias}.{cond.ColumnName}";

        int colIdx = _availableColumns.ToList().IndexOf(fullQualified);
        if (colIdx >= 0)
        {
            cmbColumn.SelectedIndex = colIdx;
        }
        else if (_availableColumns.Count > 0)
        {
            cmbColumn.SelectedIndex = 0;
            var (alias, col) = SplitQualifiedColumn(_availableColumns[0]);
            cond.TableAlias = alias;
            cond.ColumnName = col;
        }

        cmbColumn.SelectionChanged += (s, e) =>
        {
            if (cmbColumn.SelectedItem is string colEntry)
            {
                var (alias, col) = SplitQualifiedColumn(colEntry);
                cond.TableAlias = alias;
                cond.ColumnName = col;
                FilterChanged?.Invoke();
            }
        };
        Grid.SetColumn(cmbColumn, 0);
        rowGrid.Children.Add(cmbColumn);

        // 2. Operator Selector
        var cmbOperator = new ComboBox
        {
            ItemsSource = Operators,
            FontSize = 10.5,
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        int opIdx = Array.IndexOf(Operators, cond.ComparisonOperator.ToUpperInvariant());
        cmbOperator.SelectedIndex = opIdx >= 0 ? opIdx : 0;

        Grid.SetColumn(cmbOperator, 1);
        rowGrid.Children.Add(cmbOperator);

        // 3. Primary Value Input
        var txtValue = new TextBox
        {
            Text = cond.Value,
            Watermark = "Valor...",
            FontSize = 10.5,
            Padding = new Avalonia.Thickness(5, 3),
            Background = Brush.Parse("#090d14"),
            Foreground = Brush.Parse("#38bdf8"),
            BorderBrush = Brush.Parse("#1e2d42"),
            Margin = new Avalonia.Thickness(0, 0, 4, 0)
        };
        txtValue.TextChanged += (s, e) =>
        {
            cond.Value = txtValue.Text ?? string.Empty;
            FilterChanged?.Invoke();
        };
        Grid.SetColumn(txtValue, 2);
        rowGrid.Children.Add(txtValue);

        // 4. Secondary Value Input (for BETWEEN)
        var txtSecondValue = new TextBox
        {
            Text = cond.SecondValue,
            Watermark = "Até...",
            FontSize = 10.5,
            Padding = new Avalonia.Thickness(5, 3),
            Background = Brush.Parse("#090d14"),
            Foreground = Brush.Parse("#38bdf8"),
            BorderBrush = Brush.Parse("#1e2d42"),
            Margin = new Avalonia.Thickness(0, 0, 4, 0),
            Width = 90,
            IsVisible = IsBetweenOperator(cond.ComparisonOperator)
        };
        txtSecondValue.TextChanged += (s, e) =>
        {
            cond.SecondValue = txtSecondValue.Text ?? string.Empty;
            FilterChanged?.Invoke();
        };
        Grid.SetColumn(txtSecondValue, 3);
        rowGrid.Children.Add(txtSecondValue);

        // Operator change updates value box visibility
        cmbOperator.SelectionChanged += (s, e) =>
        {
            if (cmbOperator.SelectedItem is string op)
            {
                cond.ComparisonOperator = op;
                bool isNullOp = op is "IS NULL" or "IS NOT NULL";
                bool isBetween = IsBetweenOperator(op);

                txtValue.IsVisible = !isNullOp;
                txtSecondValue.IsVisible = isBetween;

                FilterChanged?.Invoke();
            }
        };

        // 5. Delete Condition Button
        var btnRemove = new Button
        {
            Content = "✕",
            Classes = { "btn-icon" },
            Padding = new Avalonia.Thickness(5, 2),
            FontSize = 10.5,
            Foreground = Brush.Parse("#f87171")
        };
        ToolTip.SetTip(btnRemove, "Remover condição");

        btnRemove.Click += (s, e) =>
        {
            if (_group != null)
            {
                _group.RemoveNode(cond);
                PanelConditions.Children.Remove(rowGrid);
                UpdateRuleCounter();
                FilterChanged?.Invoke();
            }
        };
        Grid.SetColumn(btnRemove, 4);
        rowGrid.Children.Add(btnRemove);

        PanelConditions.Children.Add(rowGrid);
    }

    private void RenderSubGroupView(VisualFilterGroup subGroup)
    {
        var subView = new FilterGroupView();
        subView.Configure(subGroup, _availableColumns, _depth + 1, isRoot: false);

        subView.FilterChanged += () => FilterChanged?.Invoke();
        subView.RemoveRequested += childView =>
        {
            if (_group != null && childView.Group != null)
            {
                _group.RemoveNode(childView.Group);
                PanelSubGroups.Children.Remove(childView);
                UpdateRuleCounter();
                FilterChanged?.Invoke();
            }
        };

        PanelSubGroups.Children.Add(subView);
    }

    private static bool IsBetweenOperator(string op)
    {
        return op.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase) ||
               op.Equals("NOT BETWEEN", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Alias, string Column) SplitQualifiedColumn(string entry)
    {
        var dot = entry.IndexOf('.');
        if (dot > 0 && dot < entry.Length - 1)
        {
            return (entry[..dot], entry[(dot + 1)..]);
        }
        return (string.Empty, entry);
    }
}
