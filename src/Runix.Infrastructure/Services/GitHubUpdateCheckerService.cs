using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Storage;

namespace Runix.Infrastructure.Services;

public sealed class GitHubUpdateCheckerService : IUpdateCheckerService
{
    public const string DefaultReleasesApiUrl = "https://api.github.com/repos/juniorschroder/Runix/releases/latest";

    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();

    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private readonly string _apiEndpoint;
    private readonly string? _skippedVersionFilePath;
    private readonly UpdateTargetPlatform _targetPlatform;

    public static GitHubUpdateCheckerService Instance { get; } = new();

    public GitHubUpdateCheckerService(
        HttpClient? httpClient = null,
        string? currentVersion = null,
        string? apiEndpoint = null,
        string? skippedVersionFilePath = null,
        UpdateTargetPlatform? targetPlatform = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
        _currentVersion = string.IsNullOrWhiteSpace(currentVersion)
            ? AppConstants.AppVersion
            : SemVerComparer.NormalizeVersionString(currentVersion);
        _apiEndpoint = string.IsNullOrWhiteSpace(apiEndpoint) ? DefaultReleasesApiUrl : apiEndpoint;
        _skippedVersionFilePath = skippedVersionFilePath;
        _targetPlatform = targetPlatform ?? DetectCurrentPlatform();
    }

    public static UpdateTargetPlatform DetectCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? UpdateTargetPlatform.MacOsArm64
                : UpdateTargetPlatform.MacOsX64;
        }

        if (OperatingSystem.IsWindows())
        {
            return UpdateTargetPlatform.WindowsX64;
        }

        if (OperatingSystem.IsLinux())
        {
            // Prefer .deb on Debian/Ubuntu-based distros, .rpm on Fedora/RHEL/SUSE
            if (File.Exists("/etc/redhat-release") || File.Exists("/etc/fedora-release") || File.Exists("/etc/SuSE-release"))
            {
                return UpdateTargetPlatform.LinuxRpmX64;
            }
            return UpdateTargetPlatform.LinuxDebAmd64;
        }

        return UpdateTargetPlatform.Unknown;
    }

    public string? GetSkippedVersion()
        => ConnectionStorageService.GetSkippedUpdateVersion(_skippedVersionFilePath);

    public void SkipVersion(string version)
        => ConnectionStorageService.SetSkippedUpdateVersion(version, _skippedVersionFilePath);

    public void ClearSkippedVersion()
        => ConnectionStorageService.SetSkippedUpdateVersion(null, _skippedVersionFilePath);

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool ignoreSkippedVersion = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (!request.Headers.UserAgent.TryParseAdd($"RunixDatabaseStudio/{_currentVersion}"))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", "RunixDatabaseStudio");
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(
                    $"GitHub API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    _currentVersion);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool isDraft = root.TryGetProperty("draft", out var draftProp) && draftProp.ValueKind == JsonValueKind.True;
            bool isPrerelease = root.TryGetProperty("prerelease", out var preProp) && preProp.ValueKind == JsonValueKind.True;

            if (isDraft || isPrerelease)
            {
                return new UpdateCheckResult
                {
                    CheckSucceeded = true,
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    LatestVersion = _currentVersion,
                    TagName = $"v{_currentVersion}"
                };
            }

            string rawTag = root.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(rawTag))
            {
                return UpdateCheckResult.Failed("GitHub release payload did not contain tag_name.", _currentVersion);
            }

            string latestVersion = SemVerComparer.NormalizeVersionString(rawTag);
            string releaseName = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString() ?? rawTag
                : rawTag;

            string releaseNotes = root.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;

            string htmlUrl = root.TryGetProperty("html_url", out var htmlProp) && htmlProp.ValueKind == JsonValueKind.String
                ? htmlProp.GetString() ?? $"{AppConstants.AppRepositoryUrl}/releases/tag/{rawTag}"
                : $"{AppConstants.AppRepositoryUrl}/releases/tag/{rawTag}";

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var pubProp) &&
                pubProp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(pubProp.GetString(), out var parsedDate))
            {
                publishedAt = parsedDate;
            }

            var assets = new List<ReleaseAssetInfo>();
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in assetsProp.EnumerateArray())
                {
                    string assetName = item.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String
                        ? an.GetString() ?? string.Empty
                        : string.Empty;
                    string downloadUrl = item.TryGetProperty("browser_download_url", out var dl) && dl.ValueKind == JsonValueKind.String
                        ? dl.GetString() ?? string.Empty
                        : string.Empty;
                    long size = item.TryGetProperty("size", out var sz) && sz.TryGetInt64(out long sVal)
                        ? sVal
                        : 0;

                    if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        assets.Add(new ReleaseAssetInfo(assetName, downloadUrl, size));
                    }
                }
            }

            bool isNewer = SemVerComparer.IsNewerVersion(latestVersion, _currentVersion);
            var skippedVersion = GetSkippedVersion();
            bool isSkipped = !ignoreSkippedVersion &&
                             !string.IsNullOrWhiteSpace(skippedVersion) &&
                             string.Equals(skippedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);

            var (recommendedName, recommendedUrl) = SemVerComparer.ResolveRecommendedAsset(
                assets,
                _targetPlatform,
                latestVersion,
                htmlUrl);

            return new UpdateCheckResult
            {
                CheckSucceeded = true,
                IsUpdateAvailable = isNewer && !isSkipped,
                IsSkippedVersion = isNewer && isSkipped,
                CurrentVersion = _currentVersion,
                LatestVersion = latestVersion,
                TagName = rawTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? rawTag : $"v{latestVersion}",
                ReleaseName = releaseName,
                ReleaseNotes = releaseNotes,
                HtmlUrl = htmlUrl,
                PublishedAt = publishedAt,
                RecommendedAssetName = recommendedName,
                RecommendedDownloadUrl = recommendedUrl,
                Assets = assets
            };
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message, _currentVersion);
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        return client;
    }
}
