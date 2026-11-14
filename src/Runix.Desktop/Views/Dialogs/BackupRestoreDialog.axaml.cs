using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Backup;
using Runix.Infrastructure.Data;

namespace Runix.Desktop.Views.Dialogs;

public partial class BackupRestoreDialog : UserControl
{
    private readonly IDatabaseService _databaseService;
    private readonly IDatabaseBackupService _backupService;
    private List<string> _orderedTables = new();

    public event Action? DatabaseRestored;

    public BackupRestoreDialog()
        : this(new SqliteDatabaseService(new ConnectionConfig()))
    {
    }

    public BackupRestoreDialog(IDatabaseService databaseService)
    {
        InitializeComponent();
        _databaseService = databaseService;
        _backupService = new DatabaseBackupService(databaseService);

        SetupUi();
        SetupEvents();
        _ = LoadTablesAsync();
    }

    private void SetupUi()
    {
        bool isSqlite = _databaseService.Config.Type == DatabaseType.SQLite;
        RbFormatSqliteBin.IsEnabled = isSqlite;

        var defaultFileName = $"runix_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
        TxtBackupOutputPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            defaultFileName);
    }

    private void SetupEvents()
    {
        BtnModeBackup.Click += (s, e) => SwitchMode(isBackup: true);
        BtnModeRestore.Click += (s, e) => SwitchMode(isBackup: false);

        RbFormatSql.IsCheckedChanged += (s, e) => UpdateOutputExtension(".sql");
        RbFormatGzip.IsCheckedChanged += (s, e) => UpdateOutputExtension(".sql.gz");
        RbFormatSqliteBin.IsCheckedChanged += (s, e) =>
        {
            UpdateOutputExtension(".db");
            PanelScopeOptions.IsEnabled = RbFormatSqliteBin.IsChecked != true;
        };

        BtnCheckAllTables.Click += (s, e) => ListBackupTables.SelectAll();
        BtnUncheckAllTables.Click += (s, e) => ListBackupTables.UnselectAll();

        BtnBrowseBackupOutput.Click += async (s, e) => await BrowseBackupOutputAsync();
        BtnBrowseRestoreInput.Click += async (s, e) => await BrowseRestoreInputAsync();

        BtnStartBackup.Click += async (s, e) => await RunBackupAsync();
        BtnStartRestore.Click += async (s, e) => await RunRestoreAsync();
    }

    private void SwitchMode(bool isBackup)
    {
        PanelBackupMode.IsVisible = isBackup;
        PanelRestoreMode.IsVisible = !isBackup;
    }

    private void UpdateOutputExtension(string newExt)
    {
        var current = TxtBackupOutputPath.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(current)) return;

        if (current.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
            current = current[..^7];
        else if (Path.HasExtension(current))
            current = Path.ChangeExtension(current, null) ?? current;

        TxtBackupOutputPath.Text = current + newExt;
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var tables = await _databaseService.GetTablesAsync();
            var fks = await _databaseService.GetForeignKeysAsync();
            var physical = tables.Where(t => !t.IsView).Select(t => t.Name).ToList();
            _orderedTables = _backupService.SortTablesTopologically(physical, fks).ToList();

            ListBackupTables.ItemsSource = _orderedTables;
            ListBackupTables.SelectAll();
        }
        catch (Exception ex)
        {
            TxtProgressStatus.Text = $"Erro ao carregar tabelas: {ex.Message}";
        }
    }

    private async Task BrowseBackupOutputAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar Arquivo de Backup",
            SuggestedFileName = Path.GetFileName(TxtBackupOutputPath.Text)
        });

        if (file != null)
        {
            TxtBackupOutputPath.Text = file.Path.LocalPath;
        }
    }

    private async Task BrowseRestoreInputAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar Arquivo de Backup (.sql, .sql.gz, .db)",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            TxtRestoreInputPath.Text = files[0].Path.LocalPath;
        }
    }

    private async Task RunBackupAsync()
    {
        var outPath = TxtBackupOutputPath.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outPath)) return;

        var format = RbFormatGzip.IsChecked == true
            ? BackupFormat.CompressedSqlGzip
            : (RbFormatSqliteBin.IsChecked == true ? BackupFormat.SqliteBinary : BackupFormat.SqlScript);

        var scope = RbScopeSchema.IsChecked == true
            ? BackupScope.SchemaOnly
            : (RbScopeData.IsChecked == true ? BackupScope.DataOnly : BackupScope.Full);

        var selectedTables = ListBackupTables.SelectedItems?.Cast<string>().ToList();

        BtnStartBackup.IsEnabled = false;
        BarBackupProgress.Value = 10;

        var progress = new Progress<BackupProgress>(p =>
        {
            TxtProgressStatus.Text = p.CurrentPhase;
            BarBackupProgress.Value = p.Percentage;
            TxtMetricsBadge.Text = $"{p.ProcessedTables}/{p.TotalTables} tabelas | {p.ProcessedRows:N0} linhas";
        });

        try
        {
            var res = await _backupService.CreateBackupAsync(new BackupOptions
            {
                OutputFilePath = outPath,
                Format = format,
                Scope = scope,
                IncludeDropStatements = ChkIncludeDrop.IsChecked == true,
                IncludeProgrammableObjects = ChkIncludeProg.IsChecked == true,
                SelectedTables = selectedTables
            }, progress);

            if (res.Success)
            {
                BarBackupProgress.Value = 100;
                TxtProgressStatus.Text = $"✓ Backup salvo com sucesso em: {res.FilePath}";
                TxtProgressStatus.Foreground = Brush.Parse("#34d399");
                TxtMetricsBadge.Text = $"{res.TablesProcessed} tabelas | {res.RowsProcessed:N0} linhas | {res.ElapsedMilliseconds} ms";
            }
            else
            {
                TxtProgressStatus.Text = $"✖ Falha no Backup: {res.ErrorMessage}";
                TxtProgressStatus.Foreground = Brush.Parse("#f87171");
            }
        }
        finally
        {
            BtnStartBackup.IsEnabled = true;
        }
    }

    private async Task RunRestoreAsync()
    {
        var inPath = TxtRestoreInputPath.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(inPath)) return;

        BtnStartRestore.IsEnabled = false;
        BarBackupProgress.Value = 20;

        var progress = new Progress<BackupProgress>(p =>
        {
            TxtProgressStatus.Text = p.CurrentPhase;
            BarBackupProgress.Value = p.Percentage;
        });

        try
        {
            var res = await _backupService.RestoreBackupAsync(new RestoreOptions
            {
                InputFilePath = inPath
            }, progress);

            if (res.Success)
            {
                BarBackupProgress.Value = 100;
                TxtProgressStatus.Text = $"✓ Restore concluído com sucesso ({res.StatementsExecuted} instruções executadas em {res.ElapsedMilliseconds} ms)!";
                TxtProgressStatus.Foreground = Brush.Parse("#34d399");
                TxtMetricsBadge.Text = $"{res.StatementsExecuted} statements | {res.ElapsedMilliseconds} ms";
                DatabaseRestored?.Invoke();
            }
            else
            {
                TxtProgressStatus.Text = $"✖ Erro no Restore (Rollback aplicado): {res.ErrorMessage}";
                TxtProgressStatus.Foreground = Brush.Parse("#f87171");
            }
        }
        finally
        {
            BtnStartRestore.IsEnabled = true;
        }
    }
}
