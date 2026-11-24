using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models.Migrations;
using Runix.Infrastructure.Services;

namespace Runix.Desktop.Views.Migrations;

public partial class MigrationsTabControl : UserControl
{
    private readonly IDatabaseService? _dbService;
    private readonly IMigrationService _migrationService;
    private IReadOnlyList<MigrationItem> _currentMigrations = Array.Empty<MigrationItem>();

    public MigrationsTabControl() : this(null!)
    {
    }

    public MigrationsTabControl(IDatabaseService dbService, IMigrationService? migrationService = null)
    {
        InitializeComponent();
        _dbService = dbService;
        _migrationService = migrationService ?? new MigrationService();

        // Default migrations folder
        string defaultDir = Path.Combine(Environment.CurrentDirectory, "migrations");
        if (!Directory.Exists(defaultDir))
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            defaultDir = Path.Combine(homeDir, ".runix", "migrations");
        }
        TxtMigrationsDirectory.Text = defaultDir;

        SetupEvents();
        Loaded += async (s, e) =>
        {
            if (_dbService != null)
            {
                await RefreshMigrationsAsync();
            }
        };
    }

    private void SetupEvents()
    {
        BtnBrowseDirectory.Click += async (s, e) => await OnBrowseDirectoryAsync();
        BtnRefreshDirectory.Click += async (s, e) => await RefreshMigrationsAsync();
        BtnOpenInFolder.Click += (s, e) => OnOpenFolder();

        GridMigrations.SelectionChanged += OnGridSelectionChanged;
        BtnCopyScriptSql.Click += OnCopyScriptSql;

        BtnDryRun.Click += (s, e) => OnOpenDryRun();
        BtnApplyPending.Click += async (s, e) => await OnApplyPendingAsync();
        ModalDryRun.CloseRequested += () => CloseModal();
    }

    public async Task RefreshMigrationsAsync()
    {
        if (_dbService == null) return;

        string dir = TxtMigrationsDirectory.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(dir))
        {
            ShowStatus("Selecione um diretório de migrações válido.", isError: true);
            return;
        }

        ProgressBusy.IsVisible = true;
        ShowStatus("Escaneando migrações...", isError: false);

        try
        {
            _currentMigrations = await _migrationService.ScanMigrationsAsync(_dbService, dir);
            GridMigrations.ItemsSource = _currentMigrations;

            int total = _currentMigrations.Count;
            int applied = _currentMigrations.Count(m => m.Status == MigrationStatus.Applied);
            int pending = _currentMigrations.Count(m => m.Status == MigrationStatus.Pending);
            int tampered = _currentMigrations.Count(m => m.Status == MigrationStatus.MismatchChecksum);

            TxtTotalCount.Text = total.ToString();
            TxtAppliedCount.Text = applied.ToString();
            TxtPendingCount.Text = pending.ToString();
            TxtTamperedCount.Text = tampered.ToString();

            bool hasTampered = tampered > 0;
            BadgeTamperedWrapper.IsVisible = hasTampered;
            PanelTamperWarning.IsVisible = hasTampered;

            if (hasTampered)
            {
                var tamperedFiles = _currentMigrations.Where(m => m.Status == MigrationStatus.MismatchChecksum)
                    .Select(m => m.ScriptFileName);
                TxtTamperDetails.Text = $"Os seguintes arquivos foram alterados após aplicação: {string.Join(", ", tamperedFiles)}. A execução foi bloqueada por integridade.";
                BtnApplyPending.IsEnabled = false;
                BtnDryRun.IsEnabled = false;
            }
            else
            {
                BtnApplyPending.IsEnabled = pending > 0;
                BtnDryRun.IsEnabled = pending > 0;
            }

            // Auto-select first pending or first item
            var firstPending = _currentMigrations.FirstOrDefault(m => m.Status == MigrationStatus.Pending)
                               ?? _currentMigrations.FirstOrDefault();
            if (firstPending != null)
            {
                GridMigrations.SelectedItem = firstPending;
            }

            ShowStatus($"Pronto. {total} migrações encontradas ({pending} pendentes).", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Erro ao escanear migrações: {ex.Message}", isError: true);
        }
        finally
        {
            ProgressBusy.IsVisible = false;
        }
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GridMigrations.SelectedItem is MigrationItem item)
        {
            TxtSelectedScriptName.Text = item.ScriptFileName;
            TxtSelectedChecksum.Text = $"SHA-256: {item.Checksum}";
            TxtSelectedSqlContent.Text = item.SqlContent ?? "";
            TxtInspectorStatus.Text = item.Status switch
            {
                MigrationStatus.Applied => $"✓ Aplicada com sucesso em {item.InstalledOn:yyyy-MM-dd HH:mm} ({item.ExecutionTimeMs} ms)",
                MigrationStatus.Pending => "⚡ Pendente — pronta para execução",
                MigrationStatus.MismatchChecksum => "⚠️ ALERTA: Checksum divergente! O script em disco foi modificado após aplicação.",
                MigrationStatus.Failed => "✗ Falhou durante execução anterior.",
                _ => ""
            };
        }
        else
        {
            TxtSelectedScriptName.Text = "Selecione uma migração";
            TxtSelectedChecksum.Text = "SHA-256: -";
            TxtSelectedSqlContent.Text = "";
            TxtInspectorStatus.Text = "";
        }
    }

    private async Task OnBrowseDirectoryAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecionar Pasta de Migrações SQL",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            TxtMigrationsDirectory.Text = folders[0].Path.LocalPath;
            await RefreshMigrationsAsync();
        }
    }

    private void OnOpenFolder()
    {
        string dir = TxtMigrationsDirectory.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            ShowStatus("Diretório não existe no sistema de arquivos.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Erro ao abrir pasta: {ex.Message}", isError: true);
        }
    }

    private async void OnCopyScriptSql(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard != null && !string.IsNullOrEmpty(TxtSelectedSqlContent.Text))
        {
            await top.Clipboard.SetTextAsync(TxtSelectedSqlContent.Text);
            TxtInspectorStatus.Text = "✓ SQL copiado para a área de transferência!";
        }
    }

    private void OnOpenDryRun()
    {
        var pending = _currentMigrations.Where(m => m.Status == MigrationStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            ShowStatus("Não há migrações pendentes para visualizar em Dry Run.", isError: false);
            return;
        }

        ModalDryRun.SetPendingMigrations(pending);
        PanelModalOverlay.IsVisible = true;
        ModalDryRun.IsVisible = true;
    }

    private void CloseModal()
    {
        PanelModalOverlay.IsVisible = false;
        ModalDryRun.IsVisible = false;
    }

    private async Task OnApplyPendingAsync()
    {
        if (_dbService == null) return;

        string dir = TxtMigrationsDirectory.Text?.Trim() ?? "";
        var pending = _currentMigrations.Where(m => m.Status == MigrationStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            ShowStatus("Nenhuma migração pendente encontrada para aplicar.", isError: false);
            return;
        }

        BtnApplyPending.IsEnabled = false;
        ProgressBusy.IsVisible = true;
        ShowStatus($"Aplicando {pending.Count} migrações pendentes...", isError: false);

        try
        {
            var result = await _migrationService.ApplyPendingAsync(_dbService, dir, pending);
            if (result.Success)
            {
                ShowStatus($"✓ {result.AppliedCount} migrações aplicadas com sucesso no banco de dados!", isError: false);
            }
            else
            {
                ShowStatus($"✗ Falha na execução: {result.ErrorMessage}", isError: true);
            }

            await RefreshMigrationsAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ Erro durante aplicação: {ex.Message}", isError: true);
        }
        finally
        {
            BtnApplyPending.IsEnabled = true;
            ProgressBusy.IsVisible = false;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        TxtStatusMessage.Text = message;
        TxtStatusMessage.Foreground = isError ? Brush.Parse("#fca5a5") : Brush.Parse("#8b9cb3");
    }
}
