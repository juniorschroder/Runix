using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class UpdateAvailableDialog : UserControl
{
    private readonly UpdateCheckResult _updateResult;
    private readonly IAppUpdateDownloaderService _downloaderService;
    private CancellationTokenSource? _downloadCts;

    public event Action? CloseRequested;
    public event Action<string>? VersionSkipped;

    public UpdateAvailableDialog() : this(new UpdateCheckResult())
    {
    }

    public UpdateAvailableDialog(UpdateCheckResult updateResult, IAppUpdateDownloaderService? downloaderService = null)
    {
        _updateResult = updateResult;
        _downloaderService = downloaderService ?? new AppUpdateDownloaderService(shutdownAction: () =>
        {
            Task.Run(async () =>
            {
                await Task.Delay(600);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown(0);
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                });
            });
        });
        InitializeComponent();
        PopulateDetails();
        SetupEvents();
    }

    private void PopulateDetails()
    {
        TxtNewTagBadge.Text = _updateResult.TagName;
        TxtCurrentVersion.Text = $"v{_updateResult.CurrentVersion}";
        TxtLatestVersion.Text = $"v{_updateResult.LatestVersion}";

        if (_updateResult.PublishedAt.HasValue)
        {
            TxtPublishedDate.Text = _updateResult.PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd");
        }

        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_updateResult.ReleaseNotes)
            ? _updateResult.ReleaseName
            : _updateResult.ReleaseNotes.Trim();

        TxtRecommendedAsset.Text = string.IsNullOrWhiteSpace(_updateResult.RecommendedAssetName)
            ? _updateResult.TagName
            : _updateResult.RecommendedAssetName;
    }

    private void SetupEvents()
    {
        BtnClose.Click += (s, e) =>
        {
            CancelDownload();
            CloseRequested?.Invoke();
        };

        BtnRemindLater.Click += (s, e) =>
        {
            CancelDownload();
            CloseRequested?.Invoke();
        };

        BtnDownloadInstaller.Click += async (s, e) => await StartDownloadAndInstallAsync();
        BtnCancelDownload.Click += (s, e) => CancelDownload();
        BtnRetryDownload.Click += async (s, e) => await StartDownloadAndInstallAsync();
        BtnFallbackBrowser.Click += (s, e) => OpenExternalUrl(!string.IsNullOrWhiteSpace(_updateResult.RecommendedDownloadUrl) ? _updateResult.RecommendedDownloadUrl : _updateResult.HtmlUrl);

        BtnViewOnGitHub.Click += (s, e) =>
        {
            OpenExternalUrl(_updateResult.HtmlUrl);
        };

        BtnSkipVersion.Click += (s, e) =>
        {
            CancelDownload();
            GitHubUpdateCheckerService.Instance.SkipVersion(_updateResult.LatestVersion);
            VersionSkipped?.Invoke(_updateResult.LatestVersion);
            CloseRequested?.Invoke();
        };
    }

    private async Task StartDownloadAndInstallAsync()
    {
        var downloadUrl = _updateResult.RecommendedDownloadUrl;
        if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            OpenExternalUrl(!string.IsNullOrWhiteSpace(_updateResult.RecommendedDownloadUrl) ? _updateResult.RecommendedDownloadUrl : _updateResult.HtmlUrl);
            return;
        }

        CardInstallerAction.IsVisible = false;
        CardDownloadError.IsVisible = false;
        CardDownloadProgress.IsVisible = true;
        BtnSkipVersion.IsEnabled = false;
        BtnRemindLater.IsEnabled = false;

        _downloadCts = new CancellationTokenSource();

        var progress = new Progress<DownloadProgressReport>(report =>
        {
            ProgressDownload.Value = report.ProgressPercentage;
            TxtDownloadStatus.Text = report.StatusMessage;

            if (report.TotalBytesToReceive.HasValue && report.TotalBytesToReceive.Value > 0)
            {
                var mbCurrent = report.BytesReceived / (1024.0 * 1024.0);
                var mbTotal = report.TotalBytesToReceive.Value / (1024.0 * 1024.0);
                TxtDownloadBytes.Text = $"{mbCurrent:F1} MB / {mbTotal:F1} MB";
            }
            else
            {
                var mbCurrent = report.BytesReceived / (1024.0 * 1024.0);
                TxtDownloadBytes.Text = $"{mbCurrent:F1} MB";
            }
        });

        try
        {
            var fileName = _updateResult.RecommendedAssetName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"Runix-Update-{_updateResult.LatestVersion}";
            }

            var installerPath = await _downloaderService.DownloadUpdateAsync(
                downloadUrl,
                fileName,
                progress,
                _downloadCts.Token);

            TxtDownloadStatus.Text = "✓ Download concluído! Iniciando instalador...";
            TxtDownloadStatus.Foreground = Avalonia.Media.Brush.Parse("#34d399");
            ProgressDownload.Value = 100;

            var launched = _downloaderService.LaunchInstallerAndExit(installerPath);
            if (!launched)
            {
                throw new InvalidOperationException("Não foi possível iniciar o instalador automaticamente.");
            }
        }
        catch (OperationCanceledException)
        {
            CardDownloadProgress.IsVisible = false;
            CardInstallerAction.IsVisible = true;
            BtnSkipVersion.IsEnabled = true;
            BtnRemindLater.IsEnabled = true;
        }
        catch (Exception ex)
        {
            CardDownloadProgress.IsVisible = false;
            CardDownloadError.IsVisible = true;
            TxtDownloadError.Text = $"Falha ao baixar: {ex.Message}";
            CardInstallerAction.IsVisible = true;
            BtnSkipVersion.IsEnabled = true;
            BtnRemindLater.IsEnabled = true;
        }
    }

    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

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
            // Fallback below
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
            // Ignore in headless environments
        }
    }
}
