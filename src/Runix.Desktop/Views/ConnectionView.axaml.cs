using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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
    private List<string> _customCategories = new();
    private readonly ObservableCollection<ConnectionCategoryGroup> _categoryGroups = new();
    private ConnectionConfig _currentConfig = new();
    public event Action<ConnectionConfig>? ConnectionRequested;
    public event Action? AboutRequested;

    public ConnectionView()
    {
        InitializeComponent();
        TreeSavedConnections.ItemsSource = _categoryGroups;
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
            RebuildCategoryTree();
            RebuildCategoryFlyout();
        };

        BtnAbout.Click += (s, e) => AboutRequested?.Invoke();

        LocalizationManager.LanguageChanged += lang =>
        {
            UpdateLanguageButton();
            RebuildCategoryTree();
            RebuildCategoryFlyout();
        };

        BtnSelectSQLite.Click += (s, e) => SwitchEngine(DatabaseType.SQLite);
        BtnSelectPostgres.Click += (s, e) => SwitchEngine(DatabaseType.PostgreSQL);
        BtnNewConnection.Click += (s, e) => CreateNewConnection();

        BtnNewCategory.Click += (s, e) =>
        {
            PanelNewCategoryBar.IsVisible = !PanelNewCategoryBar.IsVisible;
            if (PanelNewCategoryBar.IsVisible)
            {
                TxtNewCategoryInput.Text = string.Empty;
                TxtNewCategoryInput.Focus();
            }
        };

        BtnConfirmNewCategory.Click += (s, e) => ConfirmAddNewCategory();
        BtnCancelNewCategory.Click += (s, e) =>
        {
            PanelNewCategoryBar.IsVisible = false;
            TxtNewCategoryInput.Text = string.Empty;
        };

        TxtNewCategoryInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmAddNewCategory();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                PanelNewCategoryBar.IsVisible = false;
                TxtNewCategoryInput.Text = string.Empty;
            }
        };

        TxtFilterConnections.TextChanged += (s, e) => RebuildCategoryTree();

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

        TreeSavedConnections.SelectionChanged += (s, e) =>
        {
            if (TreeSavedConnections.SelectedItem is ConnectionConfig cfg)
            {
                SelectConnection(cfg);
                ConnectionStorageService.SetLastUsedConnectionId(cfg.Id);
            }
            else if (TreeSavedConnections.SelectedItem is ConnectionCategoryGroup grp)
            {
                grp.IsExpanded = !grp.IsExpanded;
            }
        };

        TreeSavedConnections.DoubleTapped += (s, e) =>
        {
            if (TreeSavedConnections.SelectedItem is ConnectionConfig cfg)
            {
                SelectConnection(cfg);
                OnConnect(this, new RoutedEventArgs());
            }
        };
    }

    private void OnAddConnectionInCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            string category = btn.Tag as string ?? string.Empty;
            CreateNewConnection(category);
            TxtConnectionName.Focus();
        }
    }

    private void ConfirmAddNewCategory()
    {
        string newCat = (TxtNewCategoryInput.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newCat))
        {
            PanelNewCategoryBar.IsVisible = false;
            return;
        }

        if (!_customCategories.Contains(newCat, StringComparer.OrdinalIgnoreCase))
        {
            _customCategories.Add(newCat);
            ConnectionStorageService.SaveCustomCategories(_customCategories);
        }

        PanelNewCategoryBar.IsVisible = false;
        TxtNewCategoryInput.Text = string.Empty;

        // Assign to current form as convenience
        TxtConnectionCategory.Text = newCat;
        _currentConfig.Category = newCat;

        RebuildCategoryTree();
        RebuildCategoryFlyout();
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
        TreeSavedConnections.SelectedItem = null;
        _savedConnections = ConnectionStorageService.LoadConnections();
        _customCategories = ConnectionStorageService.LoadCustomCategories();

        // Ensure categories present on saved connections are also tracked
        bool updatedCats = false;
        foreach (var conn in _savedConnections)
        {
            if (!string.IsNullOrWhiteSpace(conn.Category) &&
                !_customCategories.Contains(conn.Category.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                _customCategories.Add(conn.Category.Trim());
                updatedCats = true;
            }
        }
        if (updatedCats)
        {
            ConnectionStorageService.SaveCustomCategories(_customCategories);
        }

        RebuildCategoryTree();
        RebuildCategoryFlyout();

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
            SelectConnection(target);
            TreeSavedConnections.SelectedItem = target;
        }
        else
        {
            CreateNewConnection();
        }
    }

    private void RebuildCategoryTree()
    {
        string filter = (TxtFilterConnections?.Text ?? string.Empty).Trim();
        _categoryGroups.Clear();

        var allCategoryNames = _customCategories
            .Concat(_savedConnections
                .Where(c => !string.IsNullOrWhiteSpace(c.Category))
                .Select(c => c.Category.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var catName in allCategoryNames)
        {
            var matches = _savedConnections
                .Where(c => string.Equals(c.Category?.Trim(), catName, StringComparison.OrdinalIgnoreCase))
                .Where(c => MatchesConnectionFilter(c, catName, filter))
                .ToList();

            bool categoryItselfMatches = string.IsNullOrEmpty(filter) ||
                                         catName.Contains(filter, StringComparison.OrdinalIgnoreCase);

            if (matches.Count > 0 || categoryItselfMatches)
            {
                var group = new ConnectionCategoryGroup
                {
                    CategoryName = catName,
                    DisplayTitle = catName,
                    Icon = GetCategoryIcon(catName),
                    IsUncategorized = false,
                    IsExpanded = true
                };
                foreach (var conn in matches)
                {
                    group.Connections.Add(conn);
                }
                _categoryGroups.Add(group);
            }
        }

        // Uncategorized connections group
        var uncategorizedLabel = LocalizationManager.GetString("Connection.UncategorizedGroup", "Sem Categoria");
        var uncategorizedConns = _savedConnections
            .Where(c => string.IsNullOrWhiteSpace(c.Category))
            .Where(c => MatchesConnectionFilter(c, uncategorizedLabel, filter))
            .ToList();

        if (uncategorizedConns.Count > 0 || _categoryGroups.Count == 0)
        {
            var defaultGroup = new ConnectionCategoryGroup
            {
                CategoryName = string.Empty,
                DisplayTitle = uncategorizedLabel,
                Icon = "🗂️",
                IsUncategorized = true,
                IsExpanded = true
            };
            foreach (var conn in uncategorizedConns)
            {
                defaultGroup.Connections.Add(conn);
            }
            _categoryGroups.Add(defaultGroup);
        }
    }

    private static bool MatchesConnectionFilter(ConnectionConfig conn, string categoryLabel, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return conn.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               conn.DisplaySubtitle.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               categoryLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCategoryIcon(string categoryName)
    {
        string lower = categoryName.ToLowerInvariant();
        if (lower.Contains("prod")) return "🔴";
        if (lower.Contains("homolog") || lower.Contains("stag") || lower.Contains("qa")) return "🟡";
        if (lower.Contains("dev") || lower.Contains("local") || lower.Contains("test")) return "🟢";
        if (lower.Contains("analytic") || lower.Contains("bi") || lower.Contains("data")) return "📊";
        return "📂";
    }

    private void RebuildCategoryFlyout()
    {
        if (BtnCategoryPresets?.Flyout is not MenuFlyout flyout) return;
        flyout.Items.Clear();

        var defaultSuggestions = new[]
        {
            "Produção",
            "Homologação / Staging",
            "Desenvolvimento",
            "Local / Testes",
            "Analytics / BI"
        };

        var allSuggestions = _customCategories
            .Concat(_savedConnections.Where(c => !string.IsNullOrWhiteSpace(c.Category)).Select(c => c.Category.Trim()))
            .Concat(defaultSuggestions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var cat in allSuggestions)
        {
            var item = new MenuItem
            {
                Header = $"{GetCategoryIcon(cat)}  {cat}"
            };
            string captured = cat;
            item.Click += (s, e) =>
            {
                TxtConnectionCategory.Text = captured;
                _currentConfig.Category = captured;
            };
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        var clearItem = new MenuItem
        {
            Header = $"🗂️  {LocalizationManager.GetString("Connection.UncategorizedGroup", "Sem Categoria")}"
        };
        clearItem.Click += (s, e) =>
        {
            TxtConnectionCategory.Text = string.Empty;
            _currentConfig.Category = string.Empty;
        };
        flyout.Items.Add(clearItem);
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
        TxtConnectionCategory.Text = cfg.Category;
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

    private void CreateNewConnection(string initialCategory = "")
    {
        _currentConfig = new ConnectionConfig
        {
            Name = "New Connection",
            Category = initialCategory,
            Type = DatabaseType.SQLite,
            FilePath = ""
        };

        TxtConnectionName.Text = _currentConfig.Name;
        TxtConnectionCategory.Text = _currentConfig.Category;
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
        TreeSavedConnections.SelectedItem = null;
        HideStatus();
    }

    private void SyncConfigFromForm()
    {
        _currentConfig.Name = string.IsNullOrWhiteSpace(TxtConnectionName.Text) ? "Untitled Connection" : TxtConnectionName.Text;
        _currentConfig.Category = (TxtConnectionCategory.Text ?? string.Empty).Trim();
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
        TreeSavedConnections.SelectedItem = null;
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

        if (!string.IsNullOrWhiteSpace(_currentConfig.Category) &&
            !_customCategories.Contains(_currentConfig.Category, StringComparer.OrdinalIgnoreCase))
        {
            _customCategories.Add(_currentConfig.Category);
            ConnectionStorageService.SaveCustomCategories(_customCategories);
        }

        ConnectionStorageService.SaveConnections(_savedConnections);
        RebuildCategoryTree();
        RebuildCategoryFlyout();
        TreeSavedConnections.SelectedItem = _currentConfig;

        ShowStatus("Saved connection configuration.", isError: false, isLoading: false);
    }

    private void OnDeleteConnection(object? sender, RoutedEventArgs e)
    {
        if (_currentConfig != null)
        {
            var targetId = _currentConfig.Id;
            TreeSavedConnections.SelectedItem = null;
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
