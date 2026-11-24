namespace Runix.Core.Models;

/// <summary>
/// Detailed diagnostic result of testing a database connection, capturing roundtrip latency,
/// server version string, and encryption details.
/// </summary>
public record ConnectionDiagnosticsResult(
    bool Success,
    string? ErrorMessage = null,
    long LatencyMs = 0,
    string? ServerVersion = null,
    string? EncryptionDetails = null
);
