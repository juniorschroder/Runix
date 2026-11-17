using System;
using System.Threading;
using System.Threading.Tasks;

namespace Runix.Core.Interfaces;

public record DownloadProgressReport(
    long BytesReceived,
    long? TotalBytesToReceive,
    double ProgressPercentage,
    string StatusMessage);

public interface IAppUpdateDownloaderService
{
    Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string destinationFileName,
        IProgress<DownloadProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    bool LaunchInstallerAndExit(string installerFilePath);
}
