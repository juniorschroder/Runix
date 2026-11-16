using System;
using System.Collections.Generic;
using System.Linq;
using Runix.Core.Constants;

namespace Runix.Core.Models;

public enum UpdateTargetPlatform
{
    MacOsArm64,
    MacOsX64,
    WindowsX64,
    LinuxDebAmd64,
    LinuxRpmX64,
    Unknown
}

public sealed record ReleaseAssetInfo(
    string Name,
    string DownloadUrl,
    long SizeBytes = 0);

public sealed class UpdateCheckResult
{
    public bool CheckSucceeded { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public bool IsSkippedVersion { get; init; }
    public string CurrentVersion { get; init; } = AppConstants.AppVersion;
    public string LatestVersion { get; init; } = AppConstants.AppVersion;
    public string TagName { get; init; } = $"v{AppConstants.AppVersion}";
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = $"{AppConstants.AppRepositoryUrl}/releases/latest";
    public DateTimeOffset? PublishedAt { get; init; }
    public string? RecommendedAssetName { get; init; }
    public string? RecommendedDownloadUrl { get; init; }
    public IReadOnlyList<ReleaseAssetInfo> Assets { get; init; } = Array.Empty<ReleaseAssetInfo>();
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult Failed(string errorMessage, string? currentVersion = null)
    {
        var cur = string.IsNullOrWhiteSpace(currentVersion) ? AppConstants.AppVersion : currentVersion;
        return new UpdateCheckResult
        {
            CheckSucceeded = false,
            IsUpdateAvailable = false,
            CurrentVersion = cur,
            LatestVersion = cur,
            TagName = $"v{cur}",
            ErrorMessage = errorMessage
        };
    }
}

public static class SemVerComparer
{
    public static string NormalizeVersionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0.0.0";
        }

        var cleaned = raw.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[1..].Trim();
        }

        // Strip build metadata (+...) or pre-release suffix (-...)
        int dashIdx = cleaned.IndexOf('-');
        if (dashIdx >= 0)
        {
            cleaned = cleaned[..dashIdx];
        }

        int plusIdx = cleaned.IndexOf('+');
        if (plusIdx >= 0)
        {
            cleaned = cleaned[..plusIdx];
        }

        return cleaned.Trim();
    }

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = NormalizeVersionString(raw);
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int major) || major < 0)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int minor) || minor < 0)
        {
            return false;
        }

        int patch = 0;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0))
        {
            return false;
        }

        if (parts.Length == 4)
        {
            if (!int.TryParse(parts[3], out int rev) || rev < 0)
            {
                return false;
            }
            version = new Version(major, minor, patch, rev);
            return true;
        }

        version = new Version(major, minor, patch);
        return true;
    }

    public static bool IsNewerVersion(string? candidateVersion, string? currentVersion)
    {
        if (!TryParseVersion(candidateVersion, out var candidate))
        {
            return false;
        }

        if (!TryParseVersion(currentVersion, out var current))
        {
            return false;
        }

        return candidate > current;
    }

    public static (string AssetName, string DownloadUrl) ResolveRecommendedAsset(
        IReadOnlyList<ReleaseAssetInfo>? assets,
        UpdateTargetPlatform platform,
        string latestVersion,
        string? fallbackHtmlUrl = null)
    {
        var ver = NormalizeVersionString(latestVersion);
        var tag = $"v{ver}";
        var defaultHtml = string.IsNullOrWhiteSpace(fallbackHtmlUrl)
            ? $"{AppConstants.AppRepositoryUrl}/releases/tag/{tag}"
            : fallbackHtmlUrl;

        var (expectedSuffix, defaultFileName) = platform switch
        {
            UpdateTargetPlatform.MacOsArm64 => ("osx-arm64.dmg", $"Runix-{ver}-osx-arm64.dmg"),
            UpdateTargetPlatform.MacOsX64 => ("osx-x64.dmg", $"Runix-{ver}-osx-x64.dmg"),
            UpdateTargetPlatform.WindowsX64 => ("win-x64.exe", $"Runix-Setup-{ver}-win-x64.exe"),
            UpdateTargetPlatform.LinuxDebAmd64 => ("amd64.deb", $"runix_{ver}_amd64.deb"),
            UpdateTargetPlatform.LinuxRpmX64 => ("x86_64.rpm", $"runix-{ver}-1.x86_64.rpm"),
            _ => (string.Empty, string.Empty)
        };

        if (!string.IsNullOrEmpty(expectedSuffix) && assets != null && assets.Count > 0)
        {
            var matched = assets.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.Name) &&
                a.Name.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(a.DownloadUrl));

            if (matched != null)
            {
                return (matched.Name, matched.DownloadUrl);
            }
        }

        if (!string.IsNullOrEmpty(defaultFileName))
        {
            return (defaultFileName, $"{AppConstants.AppRepositoryUrl}/releases/download/{tag}/{defaultFileName}");
        }

        return ("GitHub Release", defaultHtml);
    }
}
