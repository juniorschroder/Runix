using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class CommandPaletteDialog : UserControl
{
    private ICommandPaletteService _paletteService;
    private readonly ObservableCollection<CommandPaletteItem> _results = new();

    public event Action<CommandPaletteItem, bool>? ItemExecuted;
    public event Action? CloseRequested;

    public CommandPaletteDialog() : this(new CommandPaletteService())
    {
    }

    public CommandPaletteDialog(ICommandPaletteService paletteService)
    {
        InitializeComponent();
        _paletteService = paletteService;
        ListResults.ItemsSource = _results;

        SetupEvents();
    }

    private void SetupEvents()
    {
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();

        BackdropGrid.PointerPressed += (s, e) =>
        {
            if (ReferenceEquals(e.Source, BackdropGrid))
            {
                CloseRequested?.Invoke();
            }
        };

        BtnScopeAll.Click += (s, e) => SetScopePrefix(string.Empty);
        BtnScopeActions.Click += (s, e) => SetScopePrefix("> ");
        BtnScopeTables.Click += (s, e) => SetScopePrefix("# ");
        BtnScopeDdl.Click += (s, e) => SetScopePrefix("@ ");
        BtnScopeQueries.Click += (s, e) => SetScopePrefix("? ");

        TxtSearch.TextChanged += (s, e) => RefreshResults();

        ListResults.DoubleTapped += (s, e) => ExecuteSelected(secondaryAction: false);

        KeyDown += OnDialogKeyDown;
    }

    private void SetScopePrefix(string prefix)
    {
        TxtSearch.Text = prefix;
        TxtSearch.CaretIndex = prefix.Length;
        TxtSearch.Focus();
        RefreshResults();
    }

    public void BindService(ICommandPaletteService service)
    {
        if (service != null)
        {
            _paletteService = service;
        }
    }

    public void OpenAndFocus(string initialQuery = "")
    {
        TxtSearch.Text = initialQuery;
        RefreshResults();
        Dispatcher.UIThread.Post(() =>
        {
            TxtSearch.Focus();
            TxtSearch.CaretIndex = (TxtSearch.Text ?? string.Empty).Length;
        }, DispatcherPriority.Input);
    }

    public void RefreshResults()
    {
        var matches = _paletteService.Search(TxtSearch.Text, maxResults: 40);
        _results.Clear();
        foreach (var item in matches)
        {
            _results.Add(item);
        }

        if (_results.Count > 0)
        {
            ListResults.SelectedIndex = 0;
        }

        TxtResultCount.Text = $"{_results.Count} itens encontrados";
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
            return;
        }

        if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (_results.Count > 0)
            {
                ListResults.SelectedIndex = (ListResults.SelectedIndex + 1) % _results.Count;
            }
            return;
        }

        if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (_results.Count > 0)
            {
                ListResults.SelectedIndex = ListResults.SelectedIndex <= 0
                    ? _results.Count - 1
                    : ListResults.SelectedIndex - 1;
            }
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            bool secondary = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            ExecuteSelected(secondary);
        }
    }

    private void ExecuteSelected(bool secondaryAction)
    {
        if (ListResults.SelectedItem is CommandPaletteItem selected)
        {
            _paletteService.RecordUsage(selected.Id);
            ItemExecuted?.Invoke(selected, secondaryAction);
        }
    }
}
