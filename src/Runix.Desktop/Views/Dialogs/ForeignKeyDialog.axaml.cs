using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Dialogs;

public partial class ForeignKeyDialog : UserControl
{
    private ErdGraphModel _graph = new();
    private string _sourceTable = string.Empty;

    public event Action? CancelRequested;
    public event Action<ForeignKeyDefinition>? ConfirmRequested;

    private static readonly string[] RuleActions = { "NO ACTION", "CASCADE", "SET NULL", "RESTRICT" };
    private static readonly string[] CardinalityOptions = { "1:N (Um para Muitos)", "1:1 (Um para Um)", "N:M (Muitos para Muitos)" };

    public ForeignKeyDialog()
    {
        InitializeComponent();

        CmbCardinality.ItemsSource = CardinalityOptions;
        CmbCardinality.SelectedIndex = 0;

        CmbOnDelete.ItemsSource = RuleActions;
        CmbOnDelete.SelectedIndex = 0;

        CmbOnUpdate.ItemsSource = RuleActions;
        CmbOnUpdate.SelectedIndex = 0;

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnAdvancePreview.Click += (s, e) => OnAdvanceClicked();

        CmbTargetTable.SelectionChanged += OnTargetTableChanged;
        CmbSourceCol.SelectionChanged += (s, e) => UpdateSuggestedConstraintName();
    }

    public void Setup(ErdGraphModel graph, string sourceTable, string? sourceCol = null, string? targetTable = null, string? targetCol = null)
    {
        _graph = graph;
        _sourceTable = sourceTable;
        TxtSourceTable.Text = sourceTable;
        PanelValidation.IsVisible = false;

        var sourceNode = graph.FindNode(sourceTable);
        var srcCols = sourceNode?.Columns.Select(c => c.Name).ToList() ?? new List<string>();
        CmbSourceCol.ItemsSource = srcCols;
        if (!string.IsNullOrWhiteSpace(sourceCol) && srcCols.Contains(sourceCol))
        {
            CmbSourceCol.SelectedItem = sourceCol;
        }
        else if (srcCols.Count > 0)
        {
            CmbSourceCol.SelectedIndex = 0;
        }

        var otherTables = graph.Nodes
            .Select(n => n.TableName)
            .Where(t => !string.Equals(t, sourceTable, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Se só existe uma tabela ou for auto-referência
        if (otherTables.Count == 0)
        {
            otherTables.Add(sourceTable);
        }

        CmbTargetTable.ItemsSource = otherTables;

        if (!string.IsNullOrWhiteSpace(targetTable) && otherTables.Contains(targetTable))
        {
            CmbTargetTable.SelectedItem = targetTable;
        }
        else if (otherTables.Count > 0)
        {
            CmbTargetTable.SelectedIndex = 0;
        }

        UpdateTargetCols(targetCol);
        UpdateSuggestedConstraintName();
    }

    private void OnTargetTableChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTargetCols(null);
        UpdateSuggestedConstraintName();
    }

    private void UpdateTargetCols(string? preferredCol)
    {
        var targetTable = CmbTargetTable.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(targetTable)) return;

        var targetNode = _graph.FindNode(targetTable);
        var tgtCols = targetNode?.Columns.Select(c => c.Name).ToList() ?? new List<string>();
        CmbTargetCol.ItemsSource = tgtCols;

        if (!string.IsNullOrWhiteSpace(preferredCol) && tgtCols.Contains(preferredCol))
        {
            CmbTargetCol.SelectedItem = preferredCol;
        }
        else
        {
            // Tenta selecionar a PK do destino como padrão
            var pkCol = targetNode?.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name;
            if (!string.IsNullOrWhiteSpace(pkCol) && tgtCols.Contains(pkCol))
            {
                CmbTargetCol.SelectedItem = pkCol;
            }
            else if (tgtCols.Count > 0)
            {
                CmbTargetCol.SelectedIndex = 0;
            }
        }
    }

    private void UpdateSuggestedConstraintName()
    {
        var srcCol = CmbSourceCol.SelectedItem?.ToString() ?? "col";
        TxtConstraintName.Text = ForeignKeyDefinition.GenerateDefaultConstraintName(_sourceTable, srcCol);
    }

    private void OnAdvanceClicked()
    {
        var srcCol = CmbSourceCol.SelectedItem?.ToString();
        var tgtTable = CmbTargetTable.SelectedItem?.ToString();
        var tgtCol = CmbTargetCol.SelectedItem?.ToString();
        var constraint = TxtConstraintName.Text?.Trim();

        if (string.IsNullOrWhiteSpace(srcCol) || string.IsNullOrWhiteSpace(tgtTable) || string.IsNullOrWhiteSpace(tgtCol))
        {
            ShowValidation("Selecione a coluna de origem, a tabela de destino e a coluna referenciada.");
            return;
        }

        var cardIdx = CmbCardinality.SelectedIndex;
        var cardinality = cardIdx switch
        {
            1 => ErdCardinality.OneToOne,
            2 => ErdCardinality.ManyToMany,
            _ => ErdCardinality.OneToMany
        };

        var fk = new ForeignKeyDefinition
        {
            SourceTable = _sourceTable,
            SourceColumn = srcCol,
            TargetTable = tgtTable,
            TargetColumn = tgtCol,
            ConstraintName = string.IsNullOrWhiteSpace(constraint) ? ForeignKeyDefinition.GenerateDefaultConstraintName(_sourceTable, srcCol) : constraint,
            Cardinality = cardinality,
            OnDelete = CmbOnDelete.SelectedItem?.ToString() ?? "NO ACTION",
            OnUpdate = CmbOnUpdate.SelectedItem?.ToString() ?? "NO ACTION"
        };

        PanelValidation.IsVisible = false;
        ConfirmRequested?.Invoke(fk);
    }

    private void ShowValidation(string msg)
    {
        TxtValidationMessage.Text = msg;
        PanelValidation.IsVisible = true;
    }
}
