using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Runix.Core.Constants;
using Runix.Desktop.Services;

namespace Runix.Desktop.Views.Dialogs;

public partial class AboutDialog : UserControl
{
    public event Action? CloseRequested;

    public AboutDialog()
    {
        InitializeComponent();
        PopulateMetadata();
        SetupEvents();
    }

    private void PopulateMetadata()
    {
        TxtVersionBadge.Text = $"v{AppConstants.AppVersion}";
        TxtVersionDetail.Text = $"{AppConstants.AppVersion} (Release)";
        TxtCreatorName.Text = AppConstants.AppAuthor;
        TxtLicenseDetail.Text = $"{AppConstants.AppLicense} • 100% Offline";
        TxtCopyFeedback.Text = AppConstants.AppCopyright;

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

        BtnCopySystemInfo.Click += async (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{AppConstants.AppName} v{AppConstants.AppVersion}");
                sb.AppendLine($"Creator: {AppConstants.AppAuthor}");
                sb.AppendLine($"License: {AppConstants.AppLicense} ({AppConstants.AppCopyright})");
                sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.OSArchitecture})");
                sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");

                await topLevel.Clipboard.SetTextAsync(sb.ToString());

                TxtCopyFeedback.Text = LocalizationManager.GetString(
                    "About.CopiedStatus",
                    "✓ Informações copiadas para a área de transferência!");
                TxtCopyFeedback.Foreground = Brush.Parse("#34d399");
            }
        };
    }
}
