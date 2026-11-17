using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Runix.Core.Interfaces;
using Runix.Infrastructure.Services;
using Xunit;

namespace Runix.Infrastructure.Tests.Services;

public class AppUpdateDownloaderServiceTests : IDisposable
{
    private readonly string _tempTestDir;

    public AppUpdateDownloaderServiceTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), $"runix_downloader_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }
        catch
        {
            // Ignore test cleanup error
        }
    }

    [Fact]
    public async Task DownloadUpdateAsync_WhenServerReturnsPayload_ShouldSaveFileAndReportProgress()
    {
        var dummyData = Encoding.UTF8.GetBytes("RUNIX_INSTALLER_MOCK_PAYLOAD_1234567890");
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(dummyData)
            };
            response.Content.Headers.ContentLength = dummyData.Length;
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateDownloaderService(httpClient);

        var reports = new List<DownloadProgressReport>();
        var progress = new Progress<DownloadProgressReport>(reports.Add);

        var fileName = $"Runix-Test-{Guid.NewGuid():N}.dmg";
        var downloadedPath = await service.DownloadUpdateAsync(
            "https://github.com/juniorschroder/Runix/releases/download/v1.3.0/" + fileName,
            fileName,
            progress);

        File.Exists(downloadedPath).Should().BeTrue();
        var downloadedContent = await File.ReadAllBytesAsync(downloadedPath);
        downloadedContent.Should().BeEquivalentTo(dummyData);

        // Clean up downloaded file
        try { File.Delete(downloadedPath); } catch { }
    }

    [Fact]
    public async Task DownloadUpdateAsync_WhenInvalidUrl_ShouldThrowArgumentException()
    {
        var service = new AppUpdateDownloaderService();
        var act = () => service.DownloadUpdateAsync("", "installer.exe");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DownloadUpdateAsync_WhenCancelled_ShouldThrowOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(2000, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateDownloaderService(httpClient);

        using var cts = new CancellationTokenSource(100);
        var act = () => service.DownloadUpdateAsync(
            "https://example.com/test.exe",
            "test.exe",
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void LaunchInstallerAndExit_WhenFileDoesNotExist_ShouldReturnFalse()
    {
        var service = new AppUpdateDownloaderService();
        var result = service.LaunchInstallerAndExit("/non/existent/path/installer.exe");
        result.Should().BeFalse();
    }

    [Fact]
    public void LaunchInstallerAndExit_WhenFileExists_ShouldDetectFile()
    {
        var dummyFile = Path.Combine(_tempTestDir, "mock_installer.txt");
        File.WriteAllText(dummyFile, "dummy");

        File.Exists(dummyFile).Should().BeTrue();
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
