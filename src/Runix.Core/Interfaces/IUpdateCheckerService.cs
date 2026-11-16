using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IUpdateCheckerService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool ignoreSkippedVersion = false,
        CancellationToken cancellationToken = default);

    string? GetSkippedVersion();

    void SkipVersion(string version);

    void ClearSkippedVersion();
}
