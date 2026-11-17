using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Runix.Core.Constants;
using Runix.Core.Models;
using Runix.Desktop.Services;
using Runix.Infrastructure.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class AboutDialog : UserControl
{
    public event Action? CloseRequested;
    public event Action<UpdateCheckResult>? UpdateAvailableRequested;

    public AboutDialog()
    {
        InitializeComponent();
        PopulateMetadata();
        SetupEvents();
    }

    private void PopulateMetadata()
    {
        TxtVersionBadge.Text = $"v{AppConstants.AppVersion}";
        TxtCreatorName.Text = AppConstants.AppAuthor;
        TxtLicenseDetail.Text = AppConstants.AppLicense;
        TxtCopyrightNotice.Text = AppConstants.AppCopyright;
        BorderStatusBanner.IsVisible = false;

        string osName = OperatingSystem.IsMacOS()
            ? "macOS"
            : OperatingSystem.IsWindows()
                ? "Windows"
                : OperatingSystem.IsLinux()
                    ? "Linux"
                    : RuntimeInformation.OSDescription;

        TxtOsArch.Text = $"{osName} • {RuntimeInformation.OSArchitecture}";
        TxtRuntimeBadge.Text = $".NET {Environment.Version.Major}.{Environment.Version.Minor} • C# 13";
    }

    private void SetupEvents()
    {
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnOk.Click += (s, e) => CloseRequested?.Invoke();

        BtnCreatorLinkedIn.Click += (s, e) => OpenExternalUrl(AppConstants.AppAuthorLinkedInUrl);
        BtnGitHubRepo.Click += (s, e) => OpenExternalUrl(AppConstants.AppRepositoryUrl);

        BtnCheckUpdates.Click += async (s, e) =>
        {
            BtnCheckUpdates.IsEnabled = false;
            BorderStatusBanner.IsVisible = true;
            BorderStatusBanner.Background = Brush.Parse("#0b1d33");
            BorderStatusBanner.BorderBrush = Brush.Parse("#0284c7");
            TxtStatusIcon.Text = "⏳";
            TxtCopyFeedback.Text = LocalizationManager.GetString("Update.Checking", "Verificando atualizações...");
            TxtCopyFeedback.Foreground = Brush.Parse("#38bdf8");

            var result = await GitHubUpdateCheckerService.Instance.CheckForUpdatesAsync(ignoreSkippedVersion: true);
            BtnCheckUpdates.IsEnabled = true;

            if (!result.CheckSucceeded)
            {
                BorderStatusBanner.IsVisible = true;
                BorderStatusBanner.Background = Brush.Parse("#261704");
                BorderStatusBanner.BorderBrush = Brush.Parse("#d97706");
                TxtStatusIcon.Text = "⚠";
                TxtCopyFeedback.Text = LocalizationManager.GetString("Update.CheckFailed", "Não foi possível verificar atualizações (offline).");
                TxtCopyFeedback.Foreground = Brush.Parse("#fbbf24");
                return;
            }

            if (result.IsUpdateAvailable)
            {
                BorderStatusBanner.IsVisible = false;
                UpdateAvailableRequested?.Invoke(result);
                return;
            }

            BorderStatusBanner.IsVisible = true;
            BorderStatusBanner.Background = Brush.Parse("#06281e");
            BorderStatusBanner.BorderBrush = Brush.Parse("#059669");
            TxtStatusIcon.Text = "✓";
            TxtCopyFeedback.Text = LocalizationManager.GetString("Update.UpToDate", "Você já está na versão mais recente!");
            TxtCopyFeedback.Foreground = Brush.Parse("#34d399");
        };

        BtnCopySystemInfo.Click += async (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{AppConstants.AppName} v{AppConstants.AppVersion}");
                sb.AppendLine($"Creator: {AppConstants.AppAuthor} ({AppConstants.AppAuthorLinkedInUrl})");
                sb.AppendLine($"Repository: {AppConstants.AppRepositoryUrl}");
                sb.AppendLine($"License: {AppConstants.AppLicense} ({AppConstants.AppCopyright})");
                sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.OSArchitecture})");
                sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");

                await topLevel.Clipboard.SetTextAsync(sb.ToString());

                BorderStatusBanner.IsVisible = true;
                BorderStatusBanner.Background = Brush.Parse("#06281e");
                BorderStatusBanner.BorderBrush = Brush.Parse("#059669");
                TxtStatusIcon.Text = "📋";
                TxtCopyFeedback.Text = LocalizationManager.GetString(
                    "About.CopiedStatus",
                    "Informações copiadas para a área de transferência!");
                TxtCopyFeedback.Foreground = Brush.Parse("#34d399");
            }
        };
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Launcher != null)
            {
                _ = topLevel.Launcher.LaunchUriAsync(new Uri(url));
                return;
            }
        }
        catch
        {
            // Fallback to Process.Start below
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if no browser available in headless environment
        }
    }
}
