using System;

namespace Runix.Infrastructure.Diff.Exceptions;

/// <summary>
/// Exceção disparada quando o checksum SHA-256 do envelope não confere com o payload do snapshot.
/// </summary>
public class SnapshotIntegrityException : Exception
{
    public string? FilePath { get; }
    public string ExpectedHash { get; }
    public string ActualHash { get; }

    public SnapshotIntegrityException(string? filePath, string expectedHash, string actualHash)
        : base($"Snapshot checksum mismatch. The file may be corrupted or tampered with. Expected: '{expectedHash}', Actual: '{actualHash}'.")
    {
        FilePath = filePath;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    public SnapshotIntegrityException(string expectedHash, string actualHash)
        : this(null, expectedHash, actualHash)
    {
    }
}
