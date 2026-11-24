using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class ConnectionDiagnosticsResultTests
{
    [Fact]
    public void ConnectionDiagnosticsResult_InstantiatesCorrectly_OnSuccess()
    {
        var result = new ConnectionDiagnosticsResult(
            Success: true,
            LatencyMs: 18,
            ServerVersion: "PostgreSQL 16.2",
            EncryptionDetails: "TLS 1.3 / SCRAM-SHA-256"
        );

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(18, result.LatencyMs);
        Assert.Equal("PostgreSQL 16.2", result.ServerVersion);
        Assert.Equal("TLS 1.3 / SCRAM-SHA-256", result.EncryptionDetails);
    }

    [Fact]
    public void ConnectionDiagnosticsResult_InstantiatesCorrectly_OnFailure()
    {
        var result = new ConnectionDiagnosticsResult(
            Success: false,
            ErrorMessage: "Connection refused (127.0.0.1:5432)",
            LatencyMs: 1205
        );

        Assert.False(result.Success);
        Assert.Equal("Connection refused (127.0.0.1:5432)", result.ErrorMessage);
        Assert.Equal(1205, result.LatencyMs);
        Assert.Null(result.ServerVersion);
    }
}
