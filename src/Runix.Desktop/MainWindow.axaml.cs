using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Resilience;
using Runix.Desktop.Views;
using Runix.Desktop.Views.Dialogs;

namespace Runix.Desktop;

public partial class MainWindow : Window
{
    private ConnectionView _connectionView;
    private WorkspaceView? _workspaceView;
    private IDatabaseService? _currentService;

    public WorkspaceView? CurrentWorkspaceView => _workspaceView;

    public MainWindow()
    {
        InitializeComponent();
        this.Width = AppConstants.WindowDefaultWidth;
        this.Height = AppConstants.WindowDefaultHeight;
        this.MinWidth = AppConstants.WindowMinWidth;
        this.MinHeight = AppConstants.WindowMinHeight;

        SetupCustomTitleBar();

        _connectionView = new ConnectionView();
        _connectionView.ConnectionRequested += OnConnectionRequested;
        _connectionView.AboutRequested += OpenAboutDialog;
        MainRoot.Children.Add(_connectionView);

        AddHandler(KeyDownEvent, (s, e) =>
        {
            if (e.Key == Key.Escape && OverlayGlobalModal.IsVisible)
            {
                e.Handled = true;
                CloseAboutDialog();
            }
        });
    }

    public void OpenAboutDialog()
    {
        var about = new AboutDialog();
        about.CloseRequested += CloseAboutDialog;
        GlobalModalContainer.Content = about;
        OverlayGlobalModal.IsVisible = true;
    }

    public void CloseAboutDialog()
    {
        OverlayGlobalModal.IsVisible = false;
        GlobalModalContainer.Content = null;
    }

    private void SetupCustomTitleBar()
    {
        BtnTitleBarAbout.Click += (s, e) => OpenAboutDialog();

        OverlayGlobalModal.PointerPressed += (s, e) =>
        {
            if (ReferenceEquals(e.Source, OverlayGlobalModal))
            {
                CloseAboutDialog();
            }
        };

        if (OperatingSystem.IsMacOS())
        {
            // On macOS, PreferSystemChrome allows macOS to draw the native 🔴 🟡 🟢 traffic lights on the top-left
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
            PanelTitleLeft.Margin = new Thickness(76, 0, 0, 0);
            PanelWindowControls.IsVisible = false;
        }
        else
        {
            // On Windows and Linux, NoChrome allows our custom styled RUNIX buttons on the top-right
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            PanelTitleLeft.Margin = new Thickness(14, 0, 0, 0);
            PanelWindowControls.IsVisible = true;
        }

        // Dragging the titlebar moves the window
        CustomTitleBar.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        // Double clicking titlebar toggles maximize/restore
        CustomTitleBar.DoubleTapped += (s, e) => ToggleWindowState();

        // Window control buttons (Windows & Linux)
        BtnMinimize.Click += (s, e) => WindowState = WindowState.Minimized;
        BtnMaximize.Click += (s, e) => ToggleWindowState();
        BtnCloseWindow.Click += (s, e) => Close();

        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                BtnMaximize.Content = WindowState == WindowState.Maximized ? "🗗" : "🗖";
            }
        };
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public async System.Threading.Tasks.Task ConnectToDemoAsync()
    {
        var demoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demo.db");
        if (!System.IO.File.Exists(demoPath))
        {
            demoPath = System.IO.Path.GetFullPath("demo.db");
        }

        var demoCfg = new ConnectionConfig
        {
            Name = "Cheeses Demo (SQLite)",
            Type = DatabaseType.SQLite,
            FilePath = demoPath
        };

        await ConnectConfigAsync(demoCfg);
    }

    public void ConnectToDemo()
    {
        _ = ConnectToDemoAsync();
    }

    private async void OnConnectionRequested(ConnectionConfig config)
    {
        await ConnectConfigAsync(config);
    }

    public async System.Threading.Tasks.Task ConnectConfigAsync(ConnectionConfig config)
    {
        _currentService?.Dispose();

        IDatabaseService baseService = config.Type == DatabaseType.SQLite
            ? new SqliteDatabaseService(config)
            : new PostgresDatabaseService(config);

        IDatabaseService service = new ResilientDatabaseService(baseService);

        try
        {
            await service.ConnectAsync();
            _currentService = service;

            if (!string.IsNullOrEmpty(config.Id))
            {
                Runix.Infrastructure.Storage.ConnectionStorageService.SetLastUsedConnectionId(config.Id);
            }

            _workspaceView = new WorkspaceView(service);
            _workspaceView.DisconnectRequested += OnDisconnectRequested;
            _workspaceView.AboutRequested += OpenAboutDialog;

            MainRoot.Children.Clear();
            MainRoot.Children.Add(_workspaceView);

            // Update top titlebar badge
            BadgeActiveDb.IsVisible = true;
            TxtActiveDbIcon.Text = config.EngineIcon;
            TxtActiveDbName.Text = $"{config.Name} ({config.Type})";
        }
        catch (Exception ex)
        {
            service.Dispose();
            BadgeActiveDb.IsVisible = false;

            var dialog = new Window
            {
                Title = "Connection Failed",
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brush.Parse("#141d2b"),
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(24),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = "Could not connect to database:", FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brush.Parse("#f87171") },
                        new TextBlock { Text = ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brush.Parse("#f0f6fc") },
                        new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Background = Avalonia.Media.Brush.Parse("#0064a5"), Foreground = Avalonia.Media.Brushes.White, Padding = new Avalonia.Thickness(16, 6) }
                    }
                }
            };
            if (((StackPanel)dialog.Content).Children[2] is Button okBtn)
            {
                okBtn.Click += (s, e) => dialog.Close();
            }
            await dialog.ShowDialog(this);
        }
    }

    private void OnDisconnectRequested()
    {
        _currentService?.Dispose();
        _currentService = null;
        _workspaceView = null;

        BadgeActiveDb.IsVisible = false;

        MainRoot.Children.Clear();
        MainRoot.Children.Add(_connectionView);
    }

    public RenderTargetBitmap? CaptureRenderedFrame()
    {
        try
        {
            var width = (int)Math.Max(1200, Bounds.Width > 0 ? Bounds.Width : 1200);
            var height = (int)Math.Max(800, Bounds.Height > 0 ? Bounds.Height : 800);
            var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            rtb.Render(this);
            return rtb;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CaptureRenderedFrame error: {ex.Message}");
            return null;
        }
    }
}
