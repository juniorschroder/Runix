using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Runix.Core.Models;
using Runix.Core.Services;

namespace Runix.Desktop.Views;

public partial class RecordInspectorDrawer : UserControl
{
    private readonly ObservableCollection<InspectedCellField> _fields = new();
    private InspectedCellField? _activeField;
    private bool _allowCellUpdate;

    public event Action<string, string>? CellValueApplied;
    public event Action? CloseRequested;

    public RecordInspectorDrawer()
    {
        InitializeComponent();
        ListFields.ItemsSource = _fields;

        BtnCloseDrawer.Click += (s, e) => CloseRequested?.Invoke();

        ListFields.SelectionChanged += (s, e) =>
        {
            if (ListFields.SelectedItem is InspectedCellField field)
            {
                SelectField(field);
            }
        };

        BtnJsonPretty.Click += (s, e) =>
        {
            if (CellValueFormatter.TryFormatJsonPretty(TxtValueEditor.Text, out string pretty, out string? err))
            {
                TxtValueEditor.Text = pretty;
                SetStatus("✓ JSON válido e formatado", "#34d399");
            }
            else
            {
                SetStatus($"✗ JSON inválido: {err}", "#f87171");
            }
        };

        BtnJsonMinify.Click += (s, e) =>
        {
            if (CellValueFormatter.TryFormatJsonMinified(TxtValueEditor.Text, out string minified, out string? err))
            {
                TxtValueEditor.Text = minified;
                SetStatus("✓ JSON compactado", "#34d399");
            }
            else
            {
                SetStatus($"✗ JSON inválido: {err}", "#f87171");
            }
        };

        BtnCopyValue.Click += async (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null && TxtValueEditor.Text != null)
            {
                await topLevel.Clipboard.SetTextAsync(TxtValueEditor.Text);
                SetStatus("✓ Copiado para a área de transferência!", "#38bdf8");
            }
        };

        BtnApplyEdit.Click += (s, e) =>
        {
            if (_activeField != null && _allowCellUpdate)
            {
                CellValueApplied?.Invoke(_activeField.ColumnName, TxtValueEditor.Text ?? string.Empty);
                SetStatus($"✓ Atualizado em {_activeField.ColumnName} (Staged)", "#fbbf24");
            }
        };
    }

    public void SetEditable(bool editable)
    {
        _allowCellUpdate = editable;
        BtnApplyEdit.IsVisible = editable;
        TxtValueEditor.IsReadOnly = !editable;
    }

    public void LoadRow(IReadOnlyList<string> columns, IReadOnlyList<object?> values)
    {
        string? previousColumn = _activeField?.ColumnName;
        var inspected = CellValueFormatter.BuildInspectedFields(columns, values);

        _fields.Clear();
        foreach (var f in inspected)
        {
            _fields.Add(f);
        }

        if (_fields.Count == 0)
        {
            _activeField = null;
            TxtActiveColumnName.Text = "Nenhuma linha selecionada";
            TxtValueEditor.Text = string.Empty;
            return;
        }

        var toSelect = _fields.FirstOrDefault(f => string.Equals(f.ColumnName, previousColumn, StringComparison.OrdinalIgnoreCase))
                       ?? _fields.FirstOrDefault(f => f.Category is CellValueCategory.Json or CellValueCategory.LongText or CellValueCategory.HexBlob)
                       ?? _fields[0];

        ListFields.SelectedItem = toSelect;
        SelectField(toSelect);
    }

    private void SelectField(InspectedCellField field)
    {
        _activeField = field;
        TxtActiveColumnName.Text = $"{field.ColumnName} [{field.CategoryBadge}]";
        TxtValueEditor.Text = field.FormattedDetail;
        BtnApplyEdit.IsVisible = _allowCellUpdate && field.Category != CellValueCategory.HexBlob;
        SetStatus($"{field.ByteOrCharLength:N0} caracteres/bytes", "#8b9cb3");
    }

    private void SetStatus(string text, string colorHex)
    {
        TxtStatus.Text = text;
        TxtStatus.Foreground = Brush.Parse(colorHex);
    }
}
