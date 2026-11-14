using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Runix.Core.Models;
using Runix.Desktop.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class MigrationPreviewDialog : UserControl
{
    private MigrationPlan? _currentPlan;

    public event Action? CancelRequested;
    public event Func<MigrationPlan, Task>? ExecuteRequested;

    public MigrationPreviewDialog()
    {
        InitializeComponent();

        SqlSyntaxHighlighting.ConfigureSqlEditor(EditorDdlPreview);

        BtnClose.Click += (s, e) => CancelRequested?.Invoke();
        BtnCancel.Click += (s, e) => CancelRequested?.Invoke();

        BtnCopyScript.Click += async (s, e) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null && !string.IsNullOrWhiteSpace(EditorDdlPreview.Text))
            {
                await top.Clipboard.SetTextAsync(EditorDdlPreview.Text);
                BtnCopyScript.Content = "✓ Copiado!";
                await Task.Delay(1500);
                BtnCopyScript.Content = "Copiar Script";
            }
        };

        BtnExecuteMigration.Click += async (s, e) =>
        {
            if (_currentPlan == null) return;
            BtnExecuteMigration.IsEnabled = false;
            PanelStatus.IsVisible = false;

            try
            {
                if (ExecuteRequested != null)
                {
                    await ExecuteRequested.Invoke(_currentPlan);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Erro ao executar migração: {ex.Message}");
            }
            finally
            {
                BtnExecuteMigration.IsEnabled = true;
            }
        };
    }

    public void SetPlan(MigrationPlan plan)
    {
        _currentPlan = plan;
        TxtBadgeProvider.Text = plan.DatabaseType.ToString();
        PanelRebuildWarning.IsVisible = plan.RequiresTableRebuild;
        PanelStatus.IsVisible = false;
        EditorDdlPreview.Text = plan.GeneratedSql ?? string.Empty;
    }

    public void ShowError(string message)
    {
        TxtStatusMessage.Text = message;
        PanelStatus.IsVisible = true;
    }
}
