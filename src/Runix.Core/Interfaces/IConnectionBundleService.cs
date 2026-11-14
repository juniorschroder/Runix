using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IConnectionBundleService
{
    byte[] EncryptBundle(RunixConnectionBundle bundle, string password);

    RunixConnectionBundle DecryptBundle(byte[] rcfBytes, string password);

    Task ExportToFileAsync(
        string filePath,
        RunixConnectionBundle bundle,
        string password,
        CancellationToken cancellationToken = default);

    Task<RunixConnectionBundle> ReadAndDecryptFileAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default);
}
