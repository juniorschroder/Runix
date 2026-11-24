using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Runix.Core.Models.Migrations;

namespace Runix.Desktop.Views.Migrations;

public partial class DryRunPreviewDialog : UserControl
{
    public event Action? CloseRequested;

    public DryRunPreviewDialog()
    {
        InitializeComponent();
        BtnCloseTop.Click += (s, e) => CloseRequested?.Invoke();
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnCopySql.Click += OnCopySql;
    }

    public void SetPendingMigrations(IReadOnlyList<MigrationItem> pending)
    {
        TxtPendingSummary.Text = $"{pending.Count} migrações pendentes em fila de execução";

        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine($"-- RUNIX Database Studio — Dry Run Inspector");
        sb.AppendLine($"-- Total Pending Scripts: {pending.Count}");
        sb.AppendLine($"-- Execution Sequence: {string.Join(" -> ", System.Linq.Enumerable.Select(pending, p => "V" + p.Version))}");
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine();

        int index = 1;
        foreach (var m in pending)
        {
            sb.AppendLine($"-- ----------------------------------------------------------------------------");
            sb.AppendLine($"-- [{index++}/{pending.Count}] Script: {m.ScriptFileName}");
            sb.AppendLine($"-- Version: {m.Version} | Description: {m.Description}");
            sb.AppendLine($"-- Checksum SHA-256: {m.Checksum}");
            sb.AppendLine($"-- ----------------------------------------------------------------------------");
            sb.AppendLine((m.SqlContent ?? "").Trim());
            sb.AppendLine();
        }

        TxtCombinedSql.Text = sb.ToString();
        TxtCopyStatus.Text = "";
    }

    private async void OnCopySql(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard != null && !string.IsNullOrEmpty(TxtCombinedSql.Text))
        {
            await top.Clipboard.SetTextAsync(TxtCombinedSql.Text);
            TxtCopyStatus.Text = "✓ DDL copiado para a área de transferência!";
        }
    }
}
