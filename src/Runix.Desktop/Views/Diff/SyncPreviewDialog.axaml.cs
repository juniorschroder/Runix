using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Runix.Desktop.Views.Diff;

public partial class SyncPreviewDialog : UserControl
{
    private string _currentSql = string.Empty;

    public event Action? ExecuteRequested;
    public event Action? SaveSnapshotRequested;
    public event Action? CloseRequested;

    public SyncPreviewDialog()
    {
        InitializeComponent();

        BtnCloseTop.Click += (s, e) => CloseRequested?.Invoke();

        BtnCopySql.Click += async (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null && !string.IsNullOrWhiteSpace(_currentSql))
            {
                await topLevel.Clipboard.SetTextAsync(_currentSql);
                BtnCopySql.Content = "✓ Copiado!";
            }
        };

        BtnSaveFile.Click += async (s, e) => await SaveToFileAsync();

        BtnSaveSnapshot.Click += (s, e) => SaveSnapshotRequested?.Invoke();

        BtnExecute.Click += (s, e) => ExecuteRequested?.Invoke();
    }

    public void Setup(string sqlScript, bool safeMode, bool isTargetOfflineSnapshot = false)
    {
        _currentSql = sqlScript;
        TxtSqlScript.Text = sqlScript;
        BtnCopySql.Content = "📋 Copiar SQL";
        TxtExecutionStatus.Text = string.Empty;

        bool hasDestructive = !safeMode && (sqlScript.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase) ||
                                            sqlScript.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase));
        PanelSafeWarning.IsVisible = hasDestructive;

        if (isTargetOfflineSnapshot)
        {
            BtnExecute.IsEnabled = false;
            ToolTip.SetTip(BtnExecute, "O destino é um snapshot offline. Use 'Salvar Snapshot Atualizado' ou 'Copiar DDL'.");
            BtnSaveSnapshot.IsVisible = true;
        }
        else
        {
            ToolTip.SetTip(BtnExecute, null);
            BtnSaveSnapshot.IsVisible = false;
            BtnExecute.IsEnabled = !string.IsNullOrWhiteSpace(sqlScript) && !sqlScript.StartsWith("-- Nenhuma instrução");
        }
    }

    public void SetStatus(string message, bool isError = false)
    {
        TxtExecutionStatus.Text = message;
        TxtExecutionStatus.Foreground = Avalonia.Media.Brush.Parse(isError ? "#f87171" : "#34d399");
    }

    private async System.Threading.Tasks.Task SaveToFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar Script de Sincronização DDL",
            DefaultExtension = "sql",
            SuggestedFileName = $"sync_schema_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_currentSql);
            TxtExecutionStatus.Text = $"✓ Salvo em {file.Name}";
        }
    }
}
