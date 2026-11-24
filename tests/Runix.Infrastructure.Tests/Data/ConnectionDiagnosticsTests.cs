using System;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Data;
using Runix.Infrastructure.Resilience;
using Xunit;

namespace Runix.Infrastructure.Tests.Data;

public class ConnectionDiagnosticsTests : IDisposable
{
    private readonly string _tempDbPath;

    public ConnectionDiagnosticsTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"runix_diag_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Sqlite_TestConnectionDiagnosticsAsync_ReturnsVersionAndLatency()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        };

        using var service = new SqliteDatabaseService(config);
        var diag = await service.TestConnectionDiagnosticsAsync();

        Assert.True(diag.Success);
        Assert.Null(diag.ErrorMessage);
        Assert.True(diag.LatencyMs >= 0);
        Assert.NotNull(diag.ServerVersion);
        Assert.StartsWith("SQLite 3.", diag.ServerVersion);
        Assert.Equal("Local Embedded Storage", diag.EncryptionDetails);
    }

    [Fact]
    public async Task ResilientService_DelegatesDiagnosticsCorrectly()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = _tempDbPath
        };

        using var inner = new SqliteDatabaseService(config);
        using var resilient = new ResilientDatabaseService(inner);

        var diag = await resilient.TestConnectionDiagnosticsAsync();

        Assert.True(diag.Success);
        Assert.NotNull(diag.ServerVersion);
        Assert.StartsWith("SQLite 3.", diag.ServerVersion);
    }

    [Fact]
    public async Task Sqlite_TestConnectionDiagnosticsAsync_InvalidPath_ReturnsFailure()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = ""
        };

        using var service = new SqliteDatabaseService(config);
        var diag = await service.TestConnectionDiagnosticsAsync();

        Assert.False(diag.Success);
        Assert.NotNull(diag.ErrorMessage);
    }
}
