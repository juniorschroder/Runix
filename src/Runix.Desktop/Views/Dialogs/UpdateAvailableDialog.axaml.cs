using System;
using System.Diagnostics;
using Avalonia.Controls;
using Runix.Core.Models;
using Runix.Infrastructure.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class UpdateAvailableDialog : UserControl
{
    private readonly UpdateCheckResult _updateResult;

    public event Action? CloseRequested;
    public event Action<string>? VersionSkipped;

    public UpdateAvailableDialog() : this(new UpdateCheckResult())
    {
    }

    public UpdateAvailableDialog(UpdateCheckResult updateResult)
    {
        _updateResult = updateResult;
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
        BtnClose.Click += (s, e) => CloseRequested?.Invoke();
        BtnRemindLater.Click += (s, e) => CloseRequested?.Invoke();

        BtnDownloadInstaller.Click += (s, e) =>
        {
            var url = !string.IsNullOrWhiteSpace(_updateResult.RecommendedDownloadUrl)
                ? _updateResult.RecommendedDownloadUrl
                : _updateResult.HtmlUrl;
            OpenExternalUrl(url);
        };

        BtnViewOnGitHub.Click += (s, e) =>
        {
            OpenExternalUrl(_updateResult.HtmlUrl);
        };

        BtnSkipVersion.Click += (s, e) =>
        {
            GitHubUpdateCheckerService.Instance.SkipVersion(_updateResult.LatestVersion);
            VersionSkipped?.Invoke(_updateResult.LatestVersion);
            CloseRequested?.Invoke();
        };
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
