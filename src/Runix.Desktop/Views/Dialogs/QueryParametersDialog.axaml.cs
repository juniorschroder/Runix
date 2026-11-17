using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Runix.Core.Services;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views.Dialogs;

public partial class QueryParametersDialog : UserControl
{
    private IQueryParameterService? _parameterService;
    private readonly List<(string ParamName, TextBox InputBox)> _parameterInputs = new();

    public event Action? CancelRequested;
    public event Action<Dictionary<string, string>>? ExecuteRequested;

    public QueryParametersDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnExecute.Click += (s, e) => OnExecuteClicked();

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelRequested?.Invoke();
                e.Handled = true;
            }
        };
    }

    public void Setup(IReadOnlyList<string> parameterNames, IQueryParameterService parameterService)
    {
        _parameterService = parameterService;
        _parameterInputs.Clear();
        PanelParametersList.Children.Clear();

        TxtDialogSubtitle.Text = parameterNames.Count == 1
            ? "Esta consulta contém 1 parâmetro dinâmico. Informe o valor para executar."
            : $"Esta consulta contém {parameterNames.Count} parâmetros dinâmicos. Informe os valores para executar.";

        TextBox? firstInput = null;

        foreach (var paramName in parameterNames)
        {
            var history = _parameterService.GetHistory(paramName);
            var initialValue = history.FirstOrDefault() ?? string.Empty;

            var paramCard = new Border
            {
                Background = Brush.Parse("#0e141f"),
                BorderBrush = Brush.Parse("#1e2d42"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(12, 10)
            };

            var stack = new StackPanel { Spacing = 6 };

            // Header line with badge
            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
            };

            var badge = new Border
            {
                Background = Brush.Parse("#0c2338"),
                BorderBrush = Brush.Parse("#0284c7"),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(6, 2),
                Margin = new Avalonia.Thickness(0, 0, 8, 0)
            };
            badge.Child = new TextBlock
            {
                Text = $":{paramName}",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#38bdf8")
            };
            Grid.SetColumn(badge, 0);
            headerGrid.Children.Add(badge);

            var lblName = new TextBlock
            {
                Text = paramName,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#f0f6fc"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblName, 1);
            headerGrid.Children.Add(lblName);

            stack.Children.Add(headerGrid);

            // Input TextBox
            var textBox = new TextBox
            {
                Text = initialValue,
                Watermark = $"Valor para {paramName}...",
                Background = Brush.Parse("#090d14"),
                Foreground = Brush.Parse("#f0f6fc"),
                BorderBrush = Brush.Parse("#1e2d42"),
                CornerRadius = new Avalonia.CornerRadius(5),
                Padding = new Avalonia.Thickness(10, 7),
                FontSize = 12
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OnExecuteClicked();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    CancelRequested?.Invoke();
                    e.Handled = true;
                }
            };

            stack.Children.Add(textBox);
            _parameterInputs.Add((paramName, textBox));

            if (firstInput == null)
            {
                firstInput = textBox;
            }

            // History Quick Chips (if available)
            if (history.Count > 0)
            {
                var historyWrap = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Avalonia.Thickness(0, 2, 0, 0)
                };

                var histLabel = new TextBlock
                {
                    Text = "Recentes: ",
                    FontSize = 10,
                    Foreground = Brush.Parse("#64748b"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 0, 4, 0)
                };
                historyWrap.Children.Add(histLabel);

                foreach (var histVal in history.Take(5))
                {
                    var valCaptured = histVal;
                    var chipBtn = new Button
                    {
                        Content = valCaptured,
                        FontSize = 10,
                        Padding = new Avalonia.Thickness(6, 2),
                        Margin = new Avalonia.Thickness(2, 2, 2, 2),
                        Background = Brush.Parse("#141d2b"),
                        Foreground = Brush.Parse("#93c5fd"),
                        BorderBrush = Brush.Parse("#1e2d42"),
                        BorderThickness = new Avalonia.Thickness(1),
                        CornerRadius = new Avalonia.CornerRadius(4),
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };

                    chipBtn.Click += (s, e) =>
                    {
                        textBox.Text = valCaptured;
                        textBox.Focus();
                        textBox.SelectAll();
                    };

                    historyWrap.Children.Add(chipBtn);
                }

                stack.Children.Add(historyWrap);
            }

            paramCard.Child = stack;
            PanelParametersList.Children.Add(paramCard);
        }

        // Focus first input automatically
        if (firstInput != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                firstInput.Focus();
                firstInput.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OnExecuteClicked()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, input) in _parameterInputs)
        {
            var val = (input.Text ?? string.Empty).Trim();
            values[name] = val;
        }

        if (_parameterService != null)
        {
            _parameterService.SaveHistory(values);
            QueryParameterStorage.SaveFromService(_parameterService, _parameterInputs.Select(p => p.ParamName));
        }

        ExecuteRequested?.Invoke(values);
    }
}
