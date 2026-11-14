using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Desktop.Services;
using Runix.Desktop.Views.Dialogs;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Resilience;
using Runix.Infrastructure.Storage;

namespace Runix.Desktop.Views;

public partial class ConnectionView : UserControl
{
    private List<ConnectionConfig> _savedConnections = new();
    private ConnectionConfig _currentConfig = new();
    public event Action<ConnectionConfig>? ConnectionRequested;
    public event Action? AboutRequested;

    public ConnectionView()
    {
        InitializeComponent();
        SetupEvents();
        LoadSavedConnections();
        UpdateLanguageButton();
    }

    private void SetupEvents()
    {
        BtnLanguageToggle.Click += (s, e) =>
        {
            LocalizationManager.ToggleLanguage();
            UpdateLanguageButton();
        };

        BtnAbout.Click += (s, e) => AboutRequested?.Invoke();

        LocalizationManager.LanguageChanged += lang => UpdateLanguageButton();

        BtnSelectSQLite.Click += (s, e) => SwitchEngine(DatabaseType.SQLite);
        BtnSelectPostgres.Click += (s, e) => SwitchEngine(DatabaseType.PostgreSQL);
        BtnNewConnection.Click += (s, e) => CreateNewConnection();
        BtnBrowseSqlite.Click += OnBrowseSqlite;
        BtnBrowseSshKey.Click += OnBrowseSshKey;
        BtnTestConnection.Click += OnTestConnection;
        BtnSaveConnection.Click += OnSaveConnection;
        BtnConnect.Click += OnConnect;
        BtnDeleteConnection.Click += OnDeleteConnection;
        BtnExportRcf.Click += async (s, e) => await OpenExportBundleDialogAsync();
        BtnImportRcf.Click += async (s, e) => await OpenImportBundleDialogAsync();

        ChkUseSshTunnel.IsCheckedChanged += (s, e) =>
        {
            PanelSshConfigFields.IsVisible = ChkUseSshTunnel.IsChecked == true;
        };

        ComboSshAuthMethod.SelectionChanged += (s, e) =>
        {
            bool isKey = ComboSshAuthMethod.SelectedIndex == 1;
            PanelSshPasswordAuth.IsVisible = !isKey;
            PanelSshKeyAuth.IsVisible = isKey;
        };

        ListSavedConnections.SelectionChanged += (s, e) =>
        {
            if (ListSavedConnections.SelectedItem is ConnectionConfig cfg)
            {
                SelectConnection(cfg);
                ConnectionStorageService.SetLastUsedConnectionId(cfg.Id);
            }
        };
    }

    private void UpdateLanguageButton()
    {
        TxtLanguageLabel.Text = LocalizationManager.CurrentLanguageLabel;
    }

    public void SetConnecting(bool isConnecting)
    {
        ProgressConnecting.IsVisible = isConnecting;
        BtnConnect.IsEnabled = !isConnecting;
        BtnTestConnection.IsEnabled = !isConnecting;
        BtnSaveConnection.IsEnabled = !isConnecting;
    }

    private void LoadSavedConnections()
    {
        ListSavedConnections.SelectedItem = null;
        _savedConnections = ConnectionStorageService.LoadConnections();
        ListSavedConnections.ItemsSource = null;
        ListSavedConnections.ItemsSource = _savedConnections.ToList();

        var lastUsedId = ConnectionStorageService.GetLastUsedConnectionId();
        ConnectionConfig? target = null;

        if (!string.IsNullOrEmpty(lastUsedId))
        {
            target = _savedConnections.FirstOrDefault(c => string.Equals(c.Id, lastUsedId, StringComparison.OrdinalIgnoreCase));
        }

        if (target == null && _savedConnections.Count > 0)
        {
            target = _savedConnections[0];
        }

        if (target != null)
        {
            ListSavedConnections.SelectedItem = target;
        }
        else
        {
            CreateNewConnection();
        }
    }

    private void SwitchEngine(DatabaseType type)
    {
        _currentConfig.Type = type;
        if (type == DatabaseType.SQLite)
        {
            BtnSelectSQLite.Background = Brush.Parse("#008bb9");
            BtnSelectSQLite.BorderBrush = Brush.Parse("#38bdf8");
            BtnSelectSQLite.Foreground = Brushes.White;
            BtnSelectPostgres.Background = Brush.Parse("#141d2b");
            BtnSelectPostgres.BorderBrush = Brush.Parse("#1e2d42");
            BtnSelectPostgres.Foreground = Brush.Parse("#8b9cb3");

            PanelSqliteForm.IsVisible = true;
            PanelPostgresForm.IsVisible = false;
        }
        else
        {
            BtnSelectPostgres.Background = Brush.Parse("#008bb9");
            BtnSelectPostgres.BorderBrush = Brush.Parse("#38bdf8");
            BtnSelectPostgres.Foreground = Brushes.White;
            BtnSelectSQLite.Background = Brush.Parse("#141d2b");
            BtnSelectSQLite.BorderBrush = Brush.Parse("#1e2d42");
            BtnSelectSQLite.Foreground = Brush.Parse("#8b9cb3");

            PanelSqliteForm.IsVisible = false;
            PanelPostgresForm.IsVisible = true;
        }
    }

    private void SelectConnection(ConnectionConfig cfg)
    {
        _currentConfig = cfg;
        TxtConnectionName.Text = cfg.Name;
        SwitchEngine(cfg.Type);

        TxtSqlitePath.Text = cfg.FilePath;
        TxtPgHost.Text = cfg.Host;
        TxtPgPort.Text = cfg.Port.ToString();
        TxtPgDatabase.Text = cfg.Database;
        TxtPgUser.Text = cfg.Username;
        TxtPgPassword.Text = cfg.Password;

        for (int i = 0; i < ComboPgSsl.Items.Count; i++)
        {
            if (ComboPgSsl.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), cfg.SslMode, StringComparison.OrdinalIgnoreCase))
            {
                ComboPgSsl.SelectedIndex = i;
                break;
            }
        }

        ChkUseSshTunnel.IsChecked = cfg.UseSshTunnel;
        PanelSshConfigFields.IsVisible = cfg.UseSshTunnel;
        TxtSshHost.Text = cfg.SshHost;
        TxtSshPort.Text = (cfg.SshPort > 0 ? cfg.SshPort : 22).ToString();
        TxtSshUsername.Text = cfg.SshUsername;
        ComboSshAuthMethod.SelectedIndex = cfg.SshAuthMethod == SshAuthMethod.PrivateKey ? 1 : 0;
        PanelSshPasswordAuth.IsVisible = cfg.SshAuthMethod != SshAuthMethod.PrivateKey;
        PanelSshKeyAuth.IsVisible = cfg.SshAuthMethod == SshAuthMethod.PrivateKey;
        TxtSshPassword.Text = cfg.SshPassword;
        TxtSshKeyPath.Text = cfg.SshPrivateKeyPath;
        TxtSshKeyPassphrase.Text = cfg.SshPrivateKeyPassphrase;

        BtnDeleteConnection.IsVisible = true;
        HideStatus();
    }

    private void CreateNewConnection()
    {
        _currentConfig = new ConnectionConfig
        {
            Name = "New Connection",
            Type = DatabaseType.SQLite,
            FilePath = ""
        };

        TxtConnectionName.Text = _currentConfig.Name;
        TxtSqlitePath.Text = "";
        TxtPgHost.Text = "localhost";
        TxtPgPort.Text = "5432";
        TxtPgDatabase.Text = "postgres";
        TxtPgUser.Text = "postgres";
        TxtPgPassword.Text = "";
        ComboPgSsl.SelectedIndex = 1;

        ChkUseSshTunnel.IsChecked = false;
        PanelSshConfigFields.IsVisible = false;
        TxtSshHost.Text = "";
        TxtSshPort.Text = "22";
        TxtSshUsername.Text = "";
        ComboSshAuthMethod.SelectedIndex = 0;
        TxtSshPassword.Text = "";
        TxtSshKeyPath.Text = "";
        TxtSshKeyPassphrase.Text = "";

        SwitchEngine(DatabaseType.SQLite);
        BtnDeleteConnection.IsVisible = false;
        ListSavedConnections.SelectedItem = null;
        HideStatus();
    }

    private void SyncConfigFromForm()
    {
        _currentConfig.Name = string.IsNullOrWhiteSpace(TxtConnectionName.Text) ? "Untitled Connection" : TxtConnectionName.Text;
        if (_currentConfig.Type == DatabaseType.SQLite)
        {
            _currentConfig.FilePath = TxtSqlitePath.Text ?? "";
            _currentConfig.UseSshTunnel = false;
        }
        else
        {
            _currentConfig.Host = TxtPgHost.Text ?? "localhost";
            if (int.TryParse(TxtPgPort.Text, out var port))
                _currentConfig.Port = port;
            else
                _currentConfig.Port = 5432;

            _currentConfig.Database = TxtPgDatabase.Text ?? "postgres";
            _currentConfig.Username = TxtPgUser.Text ?? "postgres";
            _currentConfig.Password = TxtPgPassword.Text ?? "";
            _currentConfig.SslMode = (ComboPgSsl.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Prefer";

            _currentConfig.UseSshTunnel = ChkUseSshTunnel.IsChecked == true;
            _currentConfig.SshHost = TxtSshHost.Text?.Trim() ?? "";
            _currentConfig.SshPort = int.TryParse(TxtSshPort.Text, out var sshPort) && sshPort > 0 ? sshPort : 22;
            _currentConfig.SshUsername = TxtSshUsername.Text?.Trim() ?? "";
            _currentConfig.SshAuthMethod = ComboSshAuthMethod.SelectedIndex == 1 ? SshAuthMethod.PrivateKey : SshAuthMethod.Password;
            _currentConfig.SshPassword = TxtSshPassword.Text ?? "";
            _currentConfig.SshPrivateKeyPath = TxtSshKeyPath.Text?.Trim() ?? "";
            _currentConfig.SshPrivateKeyPassphrase = TxtSshKeyPassphrase.Text ?? "";
        }
    }

    private async void OnBrowseSshKey(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar Chave Privada SSH",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            TxtSshKeyPath.Text = files[0].Path.LocalPath;
        }
    }

    private async void OnBrowseSqlite(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SQLite Database File",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("SQLite Databases (*.db, *.sqlite, *.sqlite3)")
                {
                    Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" }
                },
                new("All Files (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            TxtSqlitePath.Text = files[0].Path.LocalPath;
        }
    }

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        SyncConfigFromForm();
        ShowStatus("Testing connection...", isError: false, isLoading: true);

        IDatabaseService baseService = _currentConfig.Type == DatabaseType.SQLite
            ? new SqliteDatabaseService(_currentConfig)
            : new PostgresDatabaseService(_currentConfig);
        IDatabaseService service = new ResilientDatabaseService(baseService);

        try
        {
            var success = await service.TestConnectionAsync();
            if (success)
            {
                ShowStatus("✓ Connection successful!", isError: false, isLoading: false);
            }
            else
            {
                ShowStatus("✗ Connection failed. Please verify credentials/path.", isError: true, isLoading: false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"✗ Error: {ex.Message}", isError: true, isLoading: false);
        }
        finally
        {
            service.Dispose();
        }
    }

    private void OnSaveConnection(object? sender, RoutedEventArgs e)
    {
        SyncConfigFromForm();
        ListSavedConnections.SelectedItem = null;
        var existing = _savedConnections.FirstOrDefault(c => c.Id == _currentConfig.Id);
        if (existing == null)
        {
            _savedConnections.Add(_currentConfig);
        }
        else
        {
            var idx = _savedConnections.IndexOf(existing);
            _savedConnections[idx] = _currentConfig;
        }

        ConnectionStorageService.SaveConnections(_savedConnections);
        ListSavedConnections.ItemsSource = null;
        ListSavedConnections.ItemsSource = _savedConnections.ToList();
        ListSavedConnections.SelectedItem = _currentConfig;

        ShowStatus("Saved connection configuration.", isError: false, isLoading: false);
    }

    private void OnDeleteConnection(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig != null)
        {
            var targetId = _currentConfig.Id;
            ListSavedConnections.SelectedItem = null;
            ListSavedConnections.ItemsSource = null;
            _savedConnections.RemoveAll(c => c.Id == targetId);
            ConnectionStorageService.SaveConnections(_savedConnections);
            LoadSavedConnections();
        }
    }

    private void OnConnect(object? sender, RoutedEventArgs e)
    {
        SyncConfigFromForm();
        if (!string.IsNullOrEmpty(_currentConfig.Id))
        {
            ConnectionStorageService.SetLastUsedConnectionId(_currentConfig.Id);
        }
        ConnectionRequested?.Invoke(_currentConfig);
    }

    private void ShowStatus(string message, bool isError, bool isLoading)
    {
        BorderStatus.IsVisible = true;
        TxtStatusMessage.Text = message;
        if (isLoading)
        {
            BorderStatus.BorderBrush = Brush.Parse("#008bb9");
            BorderStatus.Background = Brush.Parse("#141d2b");
            TxtStatusMessage.Foreground = Brush.Parse("#008bb9");
        }
        else if (isError)
        {
            BorderStatus.BorderBrush = Brush.Parse("#f87171");
            BorderStatus.Background = Brush.Parse("#2e181b");
            TxtStatusMessage.Foreground = Brush.Parse("#fca5a5");
        }
        else
        {
            BorderStatus.BorderBrush = Brush.Parse("#34d399");
            BorderStatus.Background = Brush.Parse("#122a20");
            TxtStatusMessage.Foreground = Brush.Parse("#6ee7b7");
        }
    }

    private void HideStatus()
    {
        BorderStatus.IsVisible = false;
    }

    public async Task OpenExportBundleDialogAsync()
    {
        var dialog = new ConnectionBundleDialog();
        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };
        dialog.BundleCompleted += msg =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
            ShowStatus(msg, isError: false, isLoading: false);
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
        await dialog.SetupForExportAsync(_savedConnections);
    }

    public async Task OpenImportBundleDialogAsync(string? preselectedFile = null)
    {
        var dialog = new ConnectionBundleDialog();
        dialog.CloseRequested += () =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
        };
        dialog.BundleCompleted += msg =>
        {
            PanelModalOverlay.IsVisible = false;
            ModalContainer.Content = null;
            LoadSavedConnections();
            ShowStatus(msg, isError: false, isLoading: false);
        };

        ModalContainer.Content = dialog;
        PanelModalOverlay.IsVisible = true;
        await dialog.SetupForImportAsync(preselectedFile);
    }
}
