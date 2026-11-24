using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
    private Point _dragStartPoint;
    private ConnectionConfig? _draggedConnection;
    private bool _isDragging;
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
            PanelRenameCategoryBar.IsVisible = false;
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

        BtnConfirmRenameCategory.Click += (s, e) => ConfirmRenameCategory();
        BtnCancelRenameCategory.Click += (s, e) =>
        {
            PanelRenameCategoryBar.IsVisible = false;
            TxtRenameCategoryInput.Text = string.Empty;
        };

        TxtRenameCategoryInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmRenameCategory();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                PanelRenameCategoryBar.IsVisible = false;
                TxtRenameCategoryInput.Text = string.Empty;
            }
        };

        TxtFilterConnections.TextChanged += (s, e) => RebuildCategoryTree();

        TreeSavedConnections.AddHandler(DragDrop.DragEnterEvent, OnTreeDragOver, handledEventsToo: true);
        TreeSavedConnections.AddHandler(DragDrop.DragOverEvent, OnTreeDragOver, handledEventsToo: true);
        TreeSavedConnections.AddHandler(DragDrop.DropEvent, OnTreeDrop, handledEventsToo: true);
        TreeSavedConnections.AddHandler(DragDrop.DragLeaveEvent, OnTreeDragLeave, handledEventsToo: true);

        BtnBrowseSqlite.Click += OnBrowseSqlite;
        BtnBrowseSshKey.Click += OnBrowseSshKey;
        BtnTestConnection.Click += OnTestConnection;
        BtnSaveConnection.Click += OnSaveConnection;
        BtnConnect.Click += OnConnect;
        BtnQuickConnectTop.Click += OnConnect;
        BtnQuickTestTop.Click += OnTestConnection;
        BtnDeleteConnection.Click += OnDeleteConnection;
        BtnExportRcf.Click += async (s, e) => await OpenExportBundleDialogAsync();
        BtnImportRcf.Click += async (s, e) => await OpenImportBundleDialogAsync();

        TabsPostgresConnection.SelectionChanged += (s, e) =>
        {
            bool isSsh = TabsPostgresConnection.SelectedIndex == 1;
            BadgeSshTabActive.IsVisible = isSsh;
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

        // Purge legacy default categories from demo connections so no pre-registered categories remain
        bool connectionsChanged = false;
        foreach (var conn in _savedConnections)
        {
            if (conn.Id == "demo-sqlite" && conn.Category == "Local / Demo")
            {
                conn.Category = string.Empty;
                connectionsChanged = true;
            }
            else if (conn.Id == "local-postgres" && conn.Category == "Desenvolvimento")
            {
                conn.Category = string.Empty;
                connectionsChanged = true;
            }
        }

        _customCategories.RemoveAll(c => string.Equals(c, "Local / Demo", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(c, "Desenvolvimento", StringComparison.OrdinalIgnoreCase));

        // Ensure categories present on saved connections are also tracked in custom categories
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
        if (connectionsChanged)
        {
            ConnectionStorageService.SaveConnections(_savedConnections);
        }
        if (updatedCats || connectionsChanged)
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

        var uncategorizedLabel = LocalizationManager.GetString("Connection.UncategorizedGroup", "Geral");

        // 1. "Geral" section (always at the top)
        var uncategorizedConns = _savedConnections
            .Where(c => string.IsNullOrWhiteSpace(c.Category))
            .Where(c => MatchesConnectionFilter(c, uncategorizedLabel, filter))
            .ToList();

        bool geralMatchesFilter = string.IsNullOrEmpty(filter) ||
                                  uncategorizedLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);

        if (uncategorizedConns.Count > 0 || (geralMatchesFilter && string.IsNullOrEmpty(filter)))
        {
            var defaultGroup = new ConnectionCategoryGroup
            {
                CategoryName = string.Empty,
                DisplayTitle = uncategorizedLabel,
                Icon = "📁",
                IsUncategorized = true,
                IsExpanded = true
            };
            foreach (var conn in uncategorizedConns)
            {
                defaultGroup.Connections.Add(conn);
            }
            _categoryGroups.Add(defaultGroup);
        }

        // 2. User-created custom categories exclusively
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

        if (_categoryGroups.Count == 0 && string.IsNullOrEmpty(filter))
        {
            _categoryGroups.Add(new ConnectionCategoryGroup
            {
                CategoryName = string.Empty,
                DisplayTitle = uncategorizedLabel,
                Icon = "📁",
                IsUncategorized = true,
                IsExpanded = true
            });
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
        return "📂";
    }

    private void RebuildCategoryFlyout()
    {
        if (BtnCategoryPresets?.Flyout is not MenuFlyout flyout) return;
        flyout.Items.Clear();

        var geralLabel = LocalizationManager.GetString("Connection.UncategorizedGroup", "Geral");
        var clearItem = new MenuItem
        {
            Header = $"📁  {geralLabel}"
        };
        clearItem.Click += (s, e) =>
        {
            TxtConnectionCategory.Text = string.Empty;
            _currentConfig.Category = string.Empty;
        };
        flyout.Items.Add(clearItem);

        // Only categories exclusively created by user (NO pre-registered categories)
        var allSuggestions = _customCategories
            .Concat(_savedConnections.Where(c => !string.IsNullOrWhiteSpace(c.Category)).Select(c => c.Category.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allSuggestions.Count > 0)
        {
            flyout.Items.Add(new Separator());
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
        }

        flyout.Items.Add(new Separator());
        var newCatItem = new MenuItem
        {
            Header = "➕  Nova Categoria..."
        };
        newCatItem.Click += (s, e) =>
        {
            PanelRenameCategoryBar.IsVisible = false;
            PanelNewCategoryBar.IsVisible = true;
            TxtNewCategoryInput.Text = string.Empty;
            TxtNewCategoryInput.Focus();
        };
        flyout.Items.Add(newCatItem);
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

        TabsPostgresConnection.SelectedIndex = cfg.UseSshTunnel ? 1 : 0;
        BadgeSshTabActive.IsVisible = cfg.UseSshTunnel;
        PanelSshConfigFields.IsVisible = true;
        TxtSshHost.Text = cfg.SshHost;
        TxtSshPort.Text = (cfg.SshPort > 0 ? cfg.SshPort : 22).ToString();
        TxtSshUsername.Text = cfg.SshUsername;
        ComboSshAuthMethod.SelectedIndex = cfg.SshAuthMethod == SshAuthMethod.PrivateKey ? 1 : 0;
        PanelSshPasswordAuth.IsVisible = cfg.SshAuthMethod != SshAuthMethod.PrivateKey;
        PanelSshKeyAuth.IsVisible = cfg.SshAuthMethod == SshAuthMethod.PrivateKey;
        TxtSshPassword.Text = cfg.SshPassword;
        TxtSshKeyPath.Text = cfg.SshPrivateKeyPath;
        TxtSshKeyPassphrase.Text = cfg.SshPrivateKeyPassphrase;

        ChkReadOnly.IsChecked = cfg.IsReadOnly;
        ChkProduction.IsChecked = cfg.IsProduction;

        BtnDeleteConnection.IsVisible = true;
        HideStatus();
        UpdateQuickConnectCard();
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

        TabsPostgresConnection.SelectedIndex = 0;
        BadgeSshTabActive.IsVisible = false;
        PanelSshConfigFields.IsVisible = true;
        TxtSshHost.Text = "";
        TxtSshPort.Text = "22";
        TxtSshUsername.Text = "";
        ComboSshAuthMethod.SelectedIndex = 0;
        TxtSshPassword.Text = "";
        TxtSshKeyPath.Text = "";
        TxtSshKeyPassphrase.Text = "";

        ChkReadOnly.IsChecked = false;
        ChkProduction.IsChecked = !string.IsNullOrWhiteSpace(initialCategory) && (
            initialCategory.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
            initialCategory.Contains("produção", StringComparison.OrdinalIgnoreCase) ||
            initialCategory.Contains("production", StringComparison.OrdinalIgnoreCase));

        SwitchEngine(DatabaseType.SQLite);
        BtnDeleteConnection.IsVisible = false;
        TreeSavedConnections.SelectedItem = null;
        HideStatus();
        UpdateQuickConnectCard();
    }

    private void UpdateQuickConnectCard()
    {
        if (_currentConfig == null)
        {
            CardQuickConnect.IsVisible = false;
            return;
        }

        CardQuickConnect.IsVisible = true;
        TxtQuickName.Text = string.IsNullOrWhiteSpace(_currentConfig.Name) ? "Nova Conexão" : _currentConfig.Name;
        TxtQuickIcon.Text = _currentConfig.EngineIcon;
        TxtQuickEngineTag.Text = _currentConfig.Type == DatabaseType.SQLite ? "SQLITE" : "POSTGRESQL";

        if (_currentConfig.Type == DatabaseType.SQLite)
        {
            TxtQuickDetails.Text = string.IsNullOrWhiteSpace(_currentConfig.FilePath)
                ? "demo.db (SQLite local embutido)"
                : _currentConfig.FilePath;
        }
        else
        {
            string tunnel = _currentConfig.UseSshTunnel ? "  •  🔐 Túnel SSH" : "";
            TxtQuickDetails.Text = $"{_currentConfig.Host}:{_currentConfig.Port} / {_currentConfig.Database} ({_currentConfig.Username}){tunnel}";
        }

        bool isSaved = _savedConnections.Any(c => string.Equals(c.Id, _currentConfig.Id, StringComparison.OrdinalIgnoreCase));
        BadgeQuickSaved.IsVisible = isSaved;
    }

    private void OnDirectConnectItemClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.Tag is ConnectionConfig cfg)
        {
            SelectConnection(cfg);
            ConnectionStorageService.SetLastUsedConnectionId(cfg.Id);
            ConnectionRequested?.Invoke(cfg);
        }
    }

    private void OnContextMenuConnectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionConfig cfg)
        {
            SelectConnection(cfg);
            ConnectionStorageService.SetLastUsedConnectionId(cfg.Id);
            ConnectionRequested?.Invoke(cfg);
        }
    }

    private void OnContextMenuEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionConfig cfg)
        {
            SelectConnection(cfg);
            TxtConnectionName.Focus();
        }
    }

    private void OnContextMenuTestClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionConfig cfg)
        {
            SelectConnection(cfg);
            OnTestConnection(this, new RoutedEventArgs());
        }
    }

    private void OnContextMenuDuplicateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionConfig cfg)
        {
            var dup = new ConnectionConfig
            {
                Name = $"{cfg.Name} (Cópia)",
                Category = cfg.Category,
                Type = cfg.Type,
                FilePath = cfg.FilePath,
                Host = cfg.Host,
                Port = cfg.Port,
                Database = cfg.Database,
                Username = cfg.Username,
                Password = cfg.Password,
                SslMode = cfg.SslMode,
                UseSshTunnel = cfg.UseSshTunnel,
                SshHost = cfg.SshHost,
                SshPort = cfg.SshPort,
                SshUsername = cfg.SshUsername,
                SshAuthMethod = cfg.SshAuthMethod,
                SshPassword = cfg.SshPassword,
                SshPrivateKeyPath = cfg.SshPrivateKeyPath,
                SshPrivateKeyPassphrase = cfg.SshPrivateKeyPassphrase,
                IsReadOnly = cfg.IsReadOnly,
                IsProduction = cfg.IsProduction
            };
            _savedConnections.Add(dup);
            ConnectionStorageService.SaveConnections(_savedConnections);
            RebuildCategoryTree();
            SelectConnection(dup);
        }
    }

    private void OnContextMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionConfig cfg)
        {
            SelectConnection(cfg);
            OnDeleteConnection(this, new RoutedEventArgs());
        }
    }

    private void OnCategoryMenuAddConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionCategoryGroup grp)
        {
            CreateNewConnection(grp.IsUncategorized ? string.Empty : grp.CategoryName);
        }
    }

    private void OnCategoryMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionCategoryGroup grp && !grp.IsUncategorized)
        {
            StartRenameCategory(grp.CategoryName);
        }
    }

    private void OnCategoryMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ConnectionCategoryGroup grp && !grp.IsUncategorized)
        {
            DeleteCategory(grp.CategoryName);
        }
    }

    private void OnCategoryOptionsClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is ConnectionCategoryGroup grp)
        {
            var menu = new ContextMenu();
            var addConnItem = new MenuItem
            {
                Header = $"➕  Nova Conexão em \"{grp.DisplayTitle}\""
            };
            addConnItem.Click += (s, ev) => CreateNewConnection(grp.IsUncategorized ? string.Empty : grp.CategoryName);
            menu.Items.Add(addConnItem);

            if (!grp.IsUncategorized)
            {
                var renameItem = new MenuItem
                {
                    Header = "✏️  Renomear Categoria"
                };
                renameItem.Click += (s, ev) => StartRenameCategory(grp.CategoryName);
                menu.Items.Add(renameItem);

                menu.Items.Add(new Separator());

                var deleteItem = new MenuItem
                {
                    Header = "🗑️  Excluir Categoria (Mover para Geral)"
                };
                deleteItem.Click += (s, ev) => DeleteCategory(grp.CategoryName);
                menu.Items.Add(deleteItem);
            }

            menu.Open(btn);
        }
    }

    private void OnConnectionContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu cm)
        {
            MenuItem? moveMenu = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "MenuMoveToCategory" || (m.Header as string) == LocalizationManager.GetString("Connection.MoveToCategory", "Mover para Categoria"));
            if (moveMenu == null) return;

            moveMenu.Items.Clear();

            ConnectionConfig? target = cm.DataContext as ConnectionConfig;
            if (target == null && cm.PlacementTarget is Control c)
            {
                target = c.DataContext as ConnectionConfig;
            }

            if (target == null) return;

            var geralLabel = LocalizationManager.GetString("Connection.UncategorizedGroup", "Geral");
            var itemGeral = new MenuItem
            {
                Header = $"📁  {geralLabel}"
            };
            itemGeral.Click += (s, ev) => MoveConnectionToCategory(target, string.Empty);
            moveMenu.Items.Add(itemGeral);

            var userCats = _customCategories
                .Concat(_savedConnections.Where(conn => !string.IsNullOrWhiteSpace(conn.Category)).Select(conn => conn.Category.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(cat => cat, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (userCats.Count > 0)
            {
                moveMenu.Items.Add(new Separator());
                foreach (var cat in userCats)
                {
                    var catItem = new MenuItem
                    {
                        Header = $"📂  {cat}"
                    };
                    string targetCat = cat;
                    catItem.Click += (s, ev) => MoveConnectionToCategory(target, targetCat);
                    moveMenu.Items.Add(catItem);
                }
            }
        }
    }

    private string _categoryToRename = string.Empty;

    private void StartRenameCategory(string categoryName)
    {
        _categoryToRename = categoryName;
        PanelNewCategoryBar.IsVisible = false;
        PanelRenameCategoryBar.IsVisible = true;
        TxtRenameCategoryInput.Text = categoryName;
        TxtRenameCategoryInput.Focus();
        TxtRenameCategoryInput.SelectAll();
    }

    private void ConfirmRenameCategory()
    {
        string newName = (TxtRenameCategoryInput.Text ?? string.Empty).Trim();
        PanelRenameCategoryBar.IsVisible = false;

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, _categoryToRename, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 1. Update in custom categories list
        int idx = _customCategories.FindIndex(c => string.Equals(c, _categoryToRename, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _customCategories[idx] = newName;
        }
        else if (!_customCategories.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            _customCategories.Add(newName);
        }
        _customCategories = _customCategories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        ConnectionStorageService.SaveCustomCategories(_customCategories);

        // 2. Update all saved connections that had this category
        bool anyUpdated = false;
        foreach (var conn in _savedConnections)
        {
            if (string.Equals(conn.Category?.Trim(), _categoryToRename, StringComparison.OrdinalIgnoreCase))
            {
                conn.Category = newName;
                anyUpdated = true;
            }
        }
        if (anyUpdated)
        {
            ConnectionStorageService.SaveConnections(_savedConnections);
        }

        // 3. Update current form if active
        if (string.Equals(_currentConfig.Category?.Trim(), _categoryToRename, StringComparison.OrdinalIgnoreCase))
        {
            _currentConfig.Category = newName;
            TxtConnectionCategory.Text = newName;
        }

        RebuildCategoryTree();
        RebuildCategoryFlyout();
    }

    private void DeleteCategory(string categoryName)
    {
        // 1. Remove from _customCategories
        _customCategories.RemoveAll(c => string.Equals(c, categoryName, StringComparison.OrdinalIgnoreCase));
        ConnectionStorageService.SaveCustomCategories(_customCategories);

        // 2. Move connections that had this category to "Geral" (Category = string.Empty)
        bool anyUpdated = false;
        foreach (var conn in _savedConnections)
        {
            if (string.Equals(conn.Category?.Trim(), categoryName, StringComparison.OrdinalIgnoreCase))
            {
                conn.Category = string.Empty;
                anyUpdated = true;
            }
        }
        if (anyUpdated)
        {
            ConnectionStorageService.SaveConnections(_savedConnections);
        }

        // 3. Reset form if it had this category
        if (string.Equals(_currentConfig.Category?.Trim(), categoryName, StringComparison.OrdinalIgnoreCase))
        {
            _currentConfig.Category = string.Empty;
            TxtConnectionCategory.Text = string.Empty;
        }

        RebuildCategoryTree();
        RebuildCategoryFlyout();
    }

    private void MoveConnectionToCategory(ConnectionConfig cfg, string newCategory)
    {
        cfg.Category = newCategory;
        if (ReferenceEquals(cfg, _currentConfig) || cfg.Id == _currentConfig.Id)
        {
            _currentConfig.Category = newCategory;
            TxtConnectionCategory.Text = newCategory;
        }

        if (!string.IsNullOrWhiteSpace(newCategory) && !_customCategories.Contains(newCategory, StringComparer.OrdinalIgnoreCase))
        {
            _customCategories.Add(newCategory);
            ConnectionStorageService.SaveCustomCategories(_customCategories);
        }

        ConnectionStorageService.SaveConnections(_savedConnections);
        RebuildCategoryTree();
        RebuildCategoryFlyout();
    }

    private void OnConnectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            sender is Control c && c.DataContext is ConnectionConfig cfg)
        {
            _dragStartPoint = e.GetPosition(this);
            _draggedConnection = cfg;
        }
    }

    private async void OnConnectionPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedConnection != null && !_isDragging &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var cur = e.GetPosition(this);
            var diff = cur - _dragStartPoint;
            if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
            {
                _isDragging = true;
                _draggedConnection.IsBeingDragged = true;
                var data = new DataObject();
                data.Set("ConnectionConfigId", _draggedConnection.Id);

                try
                {
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
                }
                finally
                {
                    _isDragging = false;
                    if (_draggedConnection != null)
                    {
                        _draggedConnection.IsBeingDragged = false;
                    }
                    _draggedConnection = null;
                    ClearDropHighlights();
                }
            }
        }
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        ConnectionConfig? dragged = _draggedConnection;
        if (dragged == null && e.Data.Contains("ConnectionConfigId"))
        {
            var id = e.Data.Get("ConnectionConfigId") as string;
            if (!string.IsNullOrEmpty(id))
            {
                dragged = _savedConnections.FirstOrDefault(c => c.Id == id);
            }
        }

        if (dragged != null)
        {
            var targetGroup = FindTargetCategoryGroup(e.Source as Control);
            if (targetGroup != null)
            {
                string currentCat = dragged.Category?.Trim() ?? string.Empty;
                string targetCat = targetGroup.IsUncategorized ? string.Empty : targetGroup.CategoryName.Trim();

                // Highlight and allow dropping only when dragging to a different category
                if (!string.Equals(currentCat, targetCat, StringComparison.OrdinalIgnoreCase))
                {
                    e.DragEffects = DragDropEffects.Move;
                    e.Handled = true;
                    HighlightTargetCategory(targetGroup);
                    return;
                }
            }
        }

        ClearDropHighlights();
        e.DragEffects = DragDropEffects.None;
    }

    private void OnTreeDragLeave(object? sender, DragEventArgs e)
    {
        var pos = e.GetPosition(TreeSavedConnections);
        var bounds = TreeSavedConnections.Bounds;
        if (pos.X < 0 || pos.Y < 0 || pos.X > bounds.Width || pos.Y > bounds.Height)
        {
            ClearDropHighlights();
        }
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        ClearDropHighlights();

        string? connId = null;
        if (e.Data.Contains("ConnectionConfigId"))
        {
            connId = e.Data.Get("ConnectionConfigId") as string;
        }
        else if (_draggedConnection != null)
        {
            connId = _draggedConnection.Id;
        }

        if (string.IsNullOrEmpty(connId))
        {
            return;
        }

        var conn = _savedConnections.FirstOrDefault(c => c.Id == connId);
        if (conn == null) return;

        var targetGroup = FindTargetCategoryGroup(e.Source as Control);
        if (targetGroup != null)
        {
            string targetCategory = targetGroup.IsUncategorized ? string.Empty : targetGroup.CategoryName;

            if (!string.Equals(conn.Category?.Trim() ?? string.Empty, targetCategory.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                MoveConnectionToCategory(conn, targetCategory);
            }
            e.Handled = true;
        }
    }

    private ConnectionCategoryGroup? FindTargetCategoryGroup(Control? control)
    {
        if (control == null) return null;

        if (control.DataContext is ConnectionCategoryGroup directGroup)
        {
            return directGroup;
        }
        if (control.DataContext is ConnectionConfig directCfg)
        {
            return _categoryGroups.FirstOrDefault(g => g.Connections.Contains(directCfg));
        }

        Avalonia.Visual? current = control;
        while (current != null && current != TreeSavedConnections)
        {
            if (current is Control c)
            {
                if (c.DataContext is ConnectionCategoryGroup grp)
                {
                    return grp;
                }
                if (c.DataContext is ConnectionConfig cfg)
                {
                    var grpContaining = _categoryGroups.FirstOrDefault(g => g.Connections.Contains(cfg));
                    if (grpContaining != null)
                    {
                        return grpContaining;
                    }
                }
            }
            current = (current as Control)?.Parent as Visual ?? current.GetVisualParent();
        }
        return null;
    }

    private void HighlightTargetCategory(ConnectionCategoryGroup targetGroup)
    {
        foreach (var grp in _categoryGroups)
        {
            grp.IsDragOver = (grp == targetGroup);
        }
    }

    private void ClearDropHighlights()
    {
        foreach (var grp in _categoryGroups)
        {
            if (grp.IsDragOver)
            {
                grp.IsDragOver = false;
            }
        }
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

            _currentConfig.UseSshTunnel = TabsPostgresConnection.SelectedIndex == 1;
            _currentConfig.SshHost = TxtSshHost.Text?.Trim() ?? "";
            _currentConfig.SshPort = int.TryParse(TxtSshPort.Text, out var sshPort) && sshPort > 0 ? sshPort : 22;
            _currentConfig.SshUsername = TxtSshUsername.Text?.Trim() ?? "";
            _currentConfig.SshAuthMethod = ComboSshAuthMethod.SelectedIndex == 1 ? SshAuthMethod.PrivateKey : SshAuthMethod.Password;
            _currentConfig.SshPassword = TxtSshPassword.Text ?? "";
            _currentConfig.SshPrivateKeyPath = TxtSshKeyPath.Text?.Trim() ?? "";
            _currentConfig.SshPrivateKeyPassphrase = TxtSshKeyPassphrase.Text ?? "";
        }

        _currentConfig.IsReadOnly = ChkReadOnly.IsChecked == true;
        _currentConfig.IsProduction = ChkProduction.IsChecked == true;
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

        IDatabaseService baseService = Runix.Infrastructure.Factories.DatabaseServiceFactory.Create(_currentConfig);
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
