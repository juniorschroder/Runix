using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Desktop.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class SnippetManagerDialog : UserControl
{
    private IAppStorageService? _appStorage;
    private List<CustomSnippet> _allSnippets = new();
    private CustomSnippet? _selectedSnippet;

    public event Action? CloseRequested;
    public event Action? SnippetsUpdated;

    public SnippetManagerDialog()
    {
        InitializeComponent();

        SqlSyntaxHighlighting.ConfigureSqlEditor(EditorSnippetBody);

        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnDone.Click += (s, e) => CloseRequested?.Invoke();

        BtnNewSnippet.Click += (s, e) => PrepareNewSnippet();
        BtnSaveSnippet.Click += async (s, e) => await SaveCurrentSnippetAsync();
        BtnDeleteSnippet.Click += async (s, e) => await DeleteCurrentSnippetAsync();

        TxtFilter.TextChanged += (s, e) => ApplyFilter();

        ListSnippets.SelectionChanged += (s, e) =>
        {
            if (ListSnippets.SelectedItem is CustomSnippet snippet)
            {
                SelectSnippet(snippet);
            }
        };
    }

    public async void Setup(IAppStorageService appStorage)
    {
        _appStorage = appStorage;
        await LoadSnippetsAsync();
        PrepareNewSnippet();
    }

    public async System.Threading.Tasks.Task LoadSnippetsAsync()
    {
        if (_appStorage == null) return;

        var snippets = await _appStorage.GetCustomSnippetsAsync();
        _allSnippets = snippets.OrderBy(s => s.Prefix).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = TxtFilter.Text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(filter))
        {
            ListSnippets.ItemsSource = _allSnippets.ToList();
        }
        else
        {
            ListSnippets.ItemsSource = _allSnippets
                .Where(s => (s.Prefix != null && s.Prefix.ToLowerInvariant().Contains(filter)) ||
                            (s.Title != null && s.Title.ToLowerInvariant().Contains(filter)) ||
                            (s.Description != null && s.Description.ToLowerInvariant().Contains(filter)))
                .ToList();
        }
    }

    private void SelectSnippet(CustomSnippet snippet)
    {
        _selectedSnippet = snippet;
        TxtPrefix.Text = snippet.Prefix;
        TxtTitle.Text = snippet.Title;
        TxtDescription.Text = snippet.Description;
        EditorSnippetBody.Text = snippet.Body;

        BtnDeleteSnippet.IsVisible = true;
        PanelValidation.IsVisible = false;
    }

    private void PrepareNewSnippet()
    {
        _selectedSnippet = null;
        ListSnippets.SelectedItem = null;
        TxtPrefix.Text = string.Empty;
        TxtTitle.Text = string.Empty;
        TxtDescription.Text = string.Empty;
        EditorSnippetBody.Text = string.Empty;

        BtnDeleteSnippet.IsVisible = false;
        PanelValidation.IsVisible = false;
        TxtPrefix.Focus();
    }

    private async System.Threading.Tasks.Task SaveCurrentSnippetAsync()
    {
        if (_appStorage == null) return;

        var prefix = TxtPrefix.Text?.Trim();
        var title = TxtTitle.Text?.Trim();
        var desc = TxtDescription.Text?.Trim() ?? string.Empty;
        var body = EditorSnippetBody.Text?.Trim();

        if (string.IsNullOrWhiteSpace(prefix))
        {
            ShowValidation("Informe o prefixo de atalho (ex: selall, myjoin).");
            return;
        }

        if (prefix.Any(char.IsWhiteSpace))
        {
            ShowValidation("O prefixo de atalho não pode conter espaços.");
            return;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowValidation("Informe um título identificador para o snippet.");
            return;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            ShowValidation("O corpo do código SQL do snippet não pode estar vazio.");
            return;
        }

        var snippet = _selectedSnippet ?? new CustomSnippet();
        snippet.Prefix = prefix;
        snippet.Title = title;
        snippet.Description = desc;
        snippet.Body = body;

        await _appStorage.SaveCustomSnippetAsync(snippet);
        SnippetsUpdated?.Invoke();

        await LoadSnippetsAsync();

        // Re-select saved snippet
        var updated = _allSnippets.FirstOrDefault(s => s.Id == snippet.Id || s.Prefix == snippet.Prefix);
        if (updated != null)
        {
            ListSnippets.SelectedItem = updated;
            SelectSnippet(updated);
        }

        PanelValidation.IsVisible = false;
    }

    private async System.Threading.Tasks.Task DeleteCurrentSnippetAsync()
    {
        if (_appStorage == null || _selectedSnippet == null) return;

        await _appStorage.DeleteCustomSnippetAsync(_selectedSnippet.Id);
        SnippetsUpdated?.Invoke();

        await LoadSnippetsAsync();
        PrepareNewSnippet();
    }

    private void ShowValidation(string message)
    {
        TxtValidationMessage.Text = message;
        PanelValidation.IsVisible = true;
    }
}