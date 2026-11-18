using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Dialogs;

public partial class CreateSubjectAreaDialog : UserControl
{
    private string _selectedColor = SubjectAreaPalette.DefaultColor;
    private readonly List<Border> _swatchBorders = new();
    private double _defaultX = 100;
    private double _defaultY = 100;

    public event Action<SubjectAreaModel>? ConfirmRequested;
    public event Action? CancelRequested;

    public CreateSubjectAreaDialog()
    {
        InitializeComponent();

        BuildColorSwatches();

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnConfirm.Click += OnConfirmClick;
    }

    private void BuildColorSwatches()
    {
        PanelColorSwatches.Children.Clear();
        _swatchBorders.Clear();

        foreach (var color in SubjectAreaPalette.AllColors)
        {
            var capturedColor = color;
            var isSelected = string.Equals(capturedColor, _selectedColor, StringComparison.OrdinalIgnoreCase);

            var swatch = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = Brush.Parse(capturedColor),
                BorderBrush = isSelected ? Brushes.White : Brush.Parse("#1e2d42"),
                BorderThickness = new Thickness(isSelected ? 2.5 : 1.0),
                Margin = new Thickness(0, 0, 10, 8),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            ToolTip.SetTip(swatch, SubjectAreaPalette.GetColorName(capturedColor));

            swatch.PointerPressed += (s, e) =>
            {
                _selectedColor = capturedColor;
                UpdateSwatchSelection();
            };

            _swatchBorders.Add(swatch);
            PanelColorSwatches.Children.Add(swatch);
        }
    }

    private void UpdateSwatchSelection()
    {
        for (int i = 0; i < SubjectAreaPalette.AllColors.Count; i++)
        {
            var color = SubjectAreaPalette.AllColors[i];
            var isSelected = string.Equals(color, _selectedColor, StringComparison.OrdinalIgnoreCase);
            var swatch = _swatchBorders[i];

            swatch.BorderBrush = isSelected ? Brushes.White : Brush.Parse("#1e2d42");
            swatch.BorderThickness = new Thickness(isSelected ? 2.5 : 1.0);
        }
    }

    public void Reset(double defaultX = 100, double defaultY = 100)
    {
        _defaultX = defaultX;
        _defaultY = defaultY;
        TxtTitle.Text = string.Empty;
        TxtDescription.Text = string.Empty;
        _selectedColor = SubjectAreaPalette.DefaultColor;
        UpdateSwatchSelection();
    }

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var title = TxtTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Novo Domínio";
        }

        var area = new SubjectAreaModel
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim(),
            ColorHex = _selectedColor,
            X = _defaultX,
            Y = _defaultY,
            Width = 460,
            Height = 340
        };

        ConfirmRequested?.Invoke(area);
    }
}
