using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Dialogs;

public partial class TableEditorDialog : UserControl
{
    private ErdTableNode? _originalNode;
    private DatabaseType _dbType = DatabaseType.SQLite;

    public event Action? CancelRequested;
    public event Action<TableSchemaDiff>? ConfirmRequested;

    private static readonly string[] SqliteTypes =
    {
        "INTEGER", "TEXT", "REAL", "BLOB", "NUMERIC", "BOOLEAN", "DATETIME"
    };

    private static readonly string[] PostgresTypes =
    {
        "INTEGER", "BIGINT", "SMALLINT", "SERIAL", "BIGSERIAL", "VARCHAR(255)", "TEXT", "BOOLEAN",
        "TIMESTAMP", "TIMESTAMPTZ", "DATE", "DOUBLE PRECISION", "NUMERIC(14,2)", "BYTEA", "JSONB", "UUID"
    };

    public TableEditorDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnAddColumn.Click += (s, e) => AddColumnRow($"col_{PanelColumnsContainer.Children.Count + 1}", GetDefaultType(), false, true, "");
        BtnAdvancePreview.Click += (s, e) => OnAdvanceClicked();
    }

    public void SetModeNewTable(DatabaseType dbType)
    {
        _dbType = dbType;
        _originalNode = null;
        TxtDialogTitle.Text = "Nova Tabela";
        TxtTableName.Text = "nova_tabela";
        TxtSchema.Text = dbType == DatabaseType.PostgreSQL ? "public" : "main";
        TxtSchema.IsEnabled = dbType == DatabaseType.PostgreSQL;
        PanelValidation.IsVisible = false;

        PanelColumnsContainer.Children.Clear();
        AddColumnRow("id", _dbType == DatabaseType.PostgreSQL ? "BIGSERIAL" : "INTEGER", isPk: true, isNull: false, defVal: "");
        AddColumnRow("nome", _dbType == DatabaseType.PostgreSQL ? "VARCHAR(255)" : "TEXT", isPk: false, isNull: false, defVal: "");
    }

    public void SetModeEditTable(ErdTableNode node, DatabaseType dbType)
    {
        _dbType = dbType;
        _originalNode = node;
        TxtDialogTitle.Text = $"Editar Tabela: {node.TableName}";
        TxtTableName.Text = node.TableName;
        TxtSchema.Text = string.IsNullOrWhiteSpace(node.Schema) ? "public" : node.Schema;
        TxtSchema.IsEnabled = dbType == DatabaseType.PostgreSQL;
        PanelValidation.IsVisible = false;

        PanelColumnsContainer.Children.Clear();
        foreach (var col in node.Columns)
        {
            AddColumnRow(col.Name, col.DataType, col.IsPrimaryKey, col.IsNullable, col.DefaultValue ?? "");
        }
    }

    private string GetDefaultType() => _dbType == DatabaseType.PostgreSQL ? "VARCHAR(255)" : "TEXT";

    private void AddColumnRow(string name, string type, bool isPk, bool isNull, string defVal)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,1.5*,Auto,Auto,1.2*,Auto"),
            Margin = new Thickness(0, 2)
        };

        var txtCol = new TextBox
        {
            Text = name,
            Background = Brush.Parse("#0e141f"),
            Foreground = Brush.Parse("#f0f6fc"),
            BorderBrush = Brush.Parse("#1e2d42"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(txtCol, 0);

        var types = _dbType == DatabaseType.PostgreSQL ? PostgresTypes : SqliteTypes;
        var comboType = new ComboBox
        {
            ItemsSource = types,
            SelectedItem = types.FirstOrDefault(t => t.Equals(type, StringComparison.OrdinalIgnoreCase)) ?? types[0],
            Background = Brush.Parse("#0e141f"),
            Foreground = Brush.Parse("#f0f6fc"),
            BorderBrush = Brush.Parse("#1e2d42"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(comboType, 1);

        var chkPk = new CheckBox
        {
            Content = "PK",
            IsChecked = isPk,
            Foreground = Brush.Parse("#eab308"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(chkPk, 2);

        var chkNull = new CheckBox
        {
            Content = "Null",
            IsChecked = isNull,
            Foreground = Brush.Parse("#8b9cb3"),
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(chkNull, 3);

        var txtDef = new TextBox
        {
            Text = defVal,
            Watermark = "Default",
            Background = Brush.Parse("#0e141f"),
            Foreground = Brush.Parse("#f0f6fc"),
            BorderBrush = Brush.Parse("#1e2d42"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(txtDef, 4);

        var btnRemove = new Button
        {
            Content = "✕",
            Classes = { "btn-icon" },
            Foreground = Brush.Parse("#f87171"),
            Padding = new Thickness(4, 2)
        };
        btnRemove.Click += (s, e) => PanelColumnsContainer.Children.Remove(rowGrid);
        Grid.SetColumn(btnRemove, 5);

        rowGrid.Children.Add(txtCol);
        rowGrid.Children.Add(comboType);
        rowGrid.Children.Add(chkPk);
        rowGrid.Children.Add(chkNull);
        rowGrid.Children.Add(txtDef);
        rowGrid.Children.Add(btnRemove);

        PanelColumnsContainer.Children.Add(rowGrid);
    }

    private void OnAdvanceClicked()
    {
        var tableName = TxtTableName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            ShowValidation("O nome da tabela não pode ser vazio.");
            return;
        }

        var columns = new List<ColumnInfo>();
        int pos = 0;
        foreach (var child in PanelColumnsContainer.Children)
        {
            if (child is Grid row)
            {
                var txts = row.Children.OfType<TextBox>().ToList();
                var cmb = row.Children.OfType<ComboBox>().FirstOrDefault();
                var chks = row.Children.OfType<CheckBox>().ToList();

                var colName = txts.Count > 0 ? txts[0].Text?.Trim() : $"col_{pos}";
                var defVal = txts.Count > 1 ? txts[1].Text?.Trim() : null;
                var colType = cmb?.SelectedItem?.ToString() ?? GetDefaultType();
                var isPk = chks.Count > 0 && (chks[0].IsChecked ?? false);
                var isNull = chks.Count > 1 && (chks[1].IsChecked ?? false);

                if (string.IsNullOrWhiteSpace(colName))
                {
                    ShowValidation("Todas as colunas precisam ter um nome válido.");
                    return;
                }

                columns.Add(new ColumnInfo
                {
                    OrdinalPosition = pos++,
                    Name = colName,
                    DataType = colType,
                    IsPrimaryKey = isPk,
                    IsNullable = isNull,
                    DefaultValue = string.IsNullOrWhiteSpace(defVal) ? null : defVal
                });
            }
        }

        if (columns.Count == 0)
        {
            ShowValidation("A tabela precisa conter pelo menos uma coluna.");
            return;
        }

        var modifiedNode = new ErdTableNode
        {
            Table = new TableInfo
            {
                Name = tableName,
                Schema = TxtSchema.Text?.Trim() ?? "public"
            },
            Columns = columns
        };

        var diff = TableSchemaDiff.Calculate(_originalNode, modifiedNode);
        if (!diff.HasChanges)
        {
            ShowValidation("Nenhuma alteração detectada em relação à estrutura atual.");
            return;
        }

        PanelValidation.IsVisible = false;
        ConfirmRequested?.Invoke(diff);
    }

    private void ShowValidation(string msg)
    {
        TxtValidationMessage.Text = msg;
        PanelValidation.IsVisible = true;
    }
}
