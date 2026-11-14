using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Runix.Core.Models;

namespace Runix.Desktop.Views.Dialogs;

public partial class SaveQueryDialog : UserControl
{
    private string? _connectionId;
    private SavedQuery? _editingQuery;

    public event Action? CancelRequested;
    public event Action<SavedQuery>? SaveRequested;

    public SaveQueryDialog()
    {
        InitializeComponent();

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();
        BtnSave.Click += (s, e) => OnSaveClicked();
    }

    public void Setup(string sqlText, string? connectionId = null, SavedQuery? existing = null)
    {
        _connectionId = connectionId;
        _editingQuery = existing;
        PanelValidation.IsVisible = false;

        if (existing != null)
        {
            TxtDialogTitle.Text = "Editar Consulta Salva";
            TxtTitle.Text = existing.Title;
            TxtDescription.Text = existing.Description ?? string.Empty;
            TxtTags.Text = string.Join(", ", existing.Tags);
            ChkFavorite.IsChecked = existing.IsFavorite;
            RbScopeCurrent.IsChecked = !string.IsNullOrEmpty(existing.ConnectionId);
            RbScopeGlobal.IsChecked = string.IsNullOrEmpty(existing.ConnectionId);
            TxtSqlPreview.Text = existing.SqlText;
        }
        else
        {
            TxtDialogTitle.Text = "Salvar Consulta na Biblioteca";
            TxtTitle.Text = string.Empty;
            TxtDescription.Text = string.Empty;
            TxtTags.Text = string.Empty;
            ChkFavorite.IsChecked = false;
            RbScopeCurrent.IsChecked = true;
            RbScopeGlobal.IsChecked = false;
            TxtSqlPreview.Text = sqlText;
        }
    }

    private void OnSaveClicked()
    {
        var title = TxtTitle.Text?.Trim();
        var sql = TxtSqlPreview.Text?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowValidation("Informe um título para a consulta.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            ShowValidation("A consulta SQL não pode estar vazia.");
            return;
        }

        var tagsRaw = TxtTags.Text ?? string.Empty;
        var tags = tagsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim())
                          .Where(t => !string.IsNullOrEmpty(t))
                          .ToList();

        var query = _editingQuery ?? new SavedQuery();
        query.Title = title;
        query.Description = string.IsNullOrWhiteSpace(TxtDescription.Text) ? null : TxtDescription.Text.Trim();
        query.SqlText = sql;
        query.Tags = tags;
        query.IsFavorite = ChkFavorite.IsChecked ?? false;
        query.ConnectionId = (RbScopeCurrent.IsChecked ?? true) ? _connectionId : null;

        PanelValidation.IsVisible = false;
        SaveRequested?.Invoke(query);
    }

    private void ShowValidation(string message)
    {
        TxtValidationMessage.Text = message;
        PanelValidation.IsVisible = true;
    }
}
