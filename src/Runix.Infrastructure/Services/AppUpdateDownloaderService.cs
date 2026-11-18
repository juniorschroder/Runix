using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;

namespace Runix.Infrastructure.Services;

public class AppUpdateDownloaderService : IAppUpdateDownloaderService
{
    private static readonly Lazy<AppUpdateDownloaderService> _instance = new(() => new AppUpdateDownloaderService());
    public static AppUpdateDownloaderService Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly Action? _shutdownAction;

    public AppUpdateDownloaderService(HttpClient? httpClient = null, Action? shutdownAction = null)
    {
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Runix-Database-Studio/1.4.0");
        }

        _shutdownAction = shutdownAction;
    }

    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string destinationFileName,
        IProgress<DownloadProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be null or empty.", nameof(downloadUrl));
        }

        if (string.IsNullOrWhiteSpace(destinationFileName))
        {
            destinationFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(destinationFileName))
            {
                destinationFileName = "Runix-Update-Installer";
            }
        }

        var updatesDir = Path.Combine(Path.GetTempPath(), "RunixUpdates");
        Directory.CreateDirectory(updatesDir);
        var targetFilePath = Path.Combine(updatesDir, destinationFileName);

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var percentage = (double)totalBytesRead / totalBytes.Value * 100.0;
                progress?.Report(new DownloadProgressReport(
                    totalBytesRead,
                    totalBytes.Value,
                    percentage,
                    $"Baixando... {percentage:F0}%"));
            }
            else
            {
                progress?.Report(new DownloadProgressReport(
                    totalBytesRead,
                    null,
                    0,
                    $"Baixando... {totalBytesRead / (1024.0 * 1024.0):F1} MB"));
            }
        }

        progress?.Report(new DownloadProgressReport(
            totalBytesRead,
            totalBytes ?? totalBytesRead,
            100.0,
            "Download concluído!"));

        return targetFilePath;
    }

    public bool LaunchInstallerAndExit(string installerFilePath)
    {
        if (string.IsNullOrWhiteSpace(installerFilePath) || !File.Exists(installerFilePath))
        {
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = installerFilePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{installerFilePath}\"",
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (installerFilePath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var chmod = Process.Start("chmod", $"+x \"{installerFilePath}\"");
                        chmod?.WaitForExit(2000);
                    }
                    catch
                    {
                        // Ignore chmod error if filesystem permissions already set
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installerFilePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{installerFilePath}\"",
                        UseShellExecute = false
                    });
                }
            }

            TerminateApplication();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateDownloader] Error launching installer: {ex.Message}");
            return false;
        }
    }

    private void TerminateApplication()
    {
        if (_shutdownAction != null)
        {
            _shutdownAction();
            return;
        }

        // Small delay to allow external installer process to spawn cleanly
        Task.Run(async () =>
        {
            await Task.Delay(500);
            Environment.Exit(0);
        });
    }
}
