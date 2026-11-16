using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Services;
using Xunit;

namespace Runix.Infrastructure.Tests.Services;

public class GitHubUpdateCheckerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _skippedFile;

    public GitHubUpdateCheckerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"runix_update_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _skippedFile = Path.Combine(_tempDir, "skipped_update_version.txt");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerReleaseExists_ShouldReturnUpdateAvailableAndResolvePlatformAsset()
    {
        const string json = """
        {
          "tag_name": "v1.1.0",
          "name": "⚡ RUNIX Database Studio v1.1.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-11-16T10:00:00Z",
          "html_url": "https://github.com/juniorschroder/Runix/releases/tag/v1.1.0",
          "body": "### What's New\n- Auto-update notification",
          "assets": [
            {
              "name": "Runix-1.1.0-osx-arm64.dmg",
              "browser_download_url": "https://github.com/juniorschroder/Runix/releases/download/v1.1.0/Runix-1.1.0-osx-arm64.dmg",
              "size": 45000000
            },
            {
              "name": "Runix-Setup-1.1.0-win-x64.exe",
              "browser_download_url": "https://github.com/juniorschroder/Runix/releases/download/v1.1.0/Runix-Setup-1.1.0-win-x64.exe",
              "size": 42000000
            }
          ]
        }
        """;

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckerService(
            httpClient: httpClient,
            currentVersion: "1.0.0",
            skippedVersionFilePath: _skippedFile,
            targetPlatform: UpdateTargetPlatform.MacOsArm64);

        var result = await service.CheckForUpdatesAsync();

        result.CheckSucceeded.Should().BeTrue();
        result.IsUpdateAvailable.Should().BeTrue();
        result.IsSkippedVersion.Should().BeFalse();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestVersion.Should().Be("1.1.0");
        result.TagName.Should().Be("v1.1.0");
        result.ReleaseNotes.Should().Contain("Auto-update notification");
        result.RecommendedAssetName.Should().Be("Runix-1.1.0-osx-arm64.dmg");
        result.RecommendedDownloadUrl.Should().EndWith("Runix-1.1.0-osx-arm64.dmg");
        result.Assets.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenVersionIsSkipped_ShouldSuppressStartupUpdateUnlessIgnoredFlagIsTrue()
    {
        const string json = """
        {
          "tag_name": "v1.1.0",
          "name": "v1.1.0",
          "draft": false,
          "prerelease": false,
          "body": "Release notes"
        }
        """;

        using var httpClient = CreateMockHttpClient(HttpStatusCode.OK, json);
        var service = new GitHubUpdateCheckerService(
            httpClient: httpClient,
            currentVersion: "1.0.0",
            skippedVersionFilePath: _skippedFile,
            targetPlatform: UpdateTargetPlatform.WindowsX64);

        service.SkipVersion("v1.1.0");
        service.GetSkippedVersion().Should().Be("1.1.0");

        var startupCheck = await service.CheckForUpdatesAsync(ignoreSkippedVersion: false);
        startupCheck.CheckSucceeded.Should().BeTrue();
        startupCheck.IsUpdateAvailable.Should().BeFalse();
        startupCheck.IsSkippedVersion.Should().BeTrue();

        var manualCheck = await service.CheckForUpdatesAsync(ignoreSkippedVersion: true);
        manualCheck.CheckSucceeded.Should().BeTrue();
        manualCheck.IsUpdateAvailable.Should().BeTrue();
        manualCheck.IsSkippedVersion.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenOfflineOrNetworkFails_ShouldReturnGracefulFailedResultWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Network unreachable"));
        using var httpClient = new HttpClient(handler);

        var service = new GitHubUpdateCheckerService(
            httpClient: httpClient,
            currentVersion: "1.0.0",
            skippedVersionFilePath: _skippedFile);

        var result = await service.CheckForUpdatesAsync();

        result.CheckSucceeded.Should().BeFalse();
        result.IsUpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Network unreachable");
    }

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string responseJson)
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            }));
        return new HttpClient(handler);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
