using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Runix.Desktop.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Runix.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? explicitLang = null;
            string? screenshotPath = null;
            bool autoDemo = false;
            if (desktop.Args != null)
            {
                for (int i = 0; i < desktop.Args.Length; i++)
                {
                    if (desktop.Args[i] == "--lang" && i + 1 < desktop.Args.Length)
                    {
                        explicitLang = desktop.Args[i + 1];
                    }
                    if (desktop.Args[i] == "--screenshot" && i + 1 < desktop.Args.Length)
                    {
                        screenshotPath = desktop.Args[i + 1];
                    }
                    if (desktop.Args[i] == "--demo" || desktop.Args[i] == "--workspace")
                    {
                        autoDemo = true;
                    }
                }
            }

            LocalizationManager.Initialize(explicitLang);

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            if (autoDemo)
            {
                mainWindow.ConnectToDemo();
            }

            if (!string.IsNullOrEmpty(screenshotPath))
            {
                mainWindow.Opened += async (s, e) =>
                {
                    await Task.Delay(1200);
                    try
                    {
                        var width = (int)Math.Max(1200, mainWindow.Bounds.Width);
                        var height = (int)Math.Max(800, mainWindow.Bounds.Height);
                        var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                        rtb.Render(mainWindow);
                        var fullPath = Path.GetFullPath(screenshotPath);
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        rtb.Save(fullPath);
                        Console.WriteLine($"Screenshot successfully saved to {fullPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Screenshot failed: {ex}");
                    }
                    finally
                    {
                        Environment.Exit(0);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
