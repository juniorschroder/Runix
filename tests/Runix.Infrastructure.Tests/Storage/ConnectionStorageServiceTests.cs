using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Runix.Core.Models;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Storage;

public class ConnectionStorageServiceTests : IDisposable
{
    private readonly string _tempFile;

    public ConnectionStorageServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"runix_connections_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    [Fact]
    public void LoadConnections_FileNotFound_ReturnsDefaultConnections()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.json");

        var connections = ConnectionStorageService.LoadConnections(nonExistentPath);

        connections.Should().NotBeNull();
        connections.Should().HaveCountGreaterOrEqualTo(2);
        connections.Should().Contain(c => c.Type == DatabaseType.SQLite);
        connections.Should().Contain(c => c.Type == DatabaseType.PostgreSQL);
    }

    [Fact]
    public void SaveConnections_And_LoadConnections_RoundTripsAccurately()
    {
        var items = new List<ConnectionConfig>
        {
            new()
            {
                Name = "Prod Postgres",
                Type = DatabaseType.PostgreSQL,
                Host = "pg.company.internal",
                Port = 5432,
                Database = "main",
                Username = "app_user",
                Password = "secret_password"
            },
            new()
            {
                Name = "Local Analytics",
                Type = DatabaseType.SQLite,
                FilePath = "/tmp/analytics.db"
            }
        };

        ConnectionStorageService.SaveConnections(items, _tempFile);
        File.Exists(_tempFile).Should().BeTrue();

        var loaded = ConnectionStorageService.LoadConnections(_tempFile);

        loaded.Should().HaveCount(2);
        loaded[0].Name.Should().Be("Prod Postgres");
        loaded[0].Type.Should().Be(DatabaseType.PostgreSQL);
        loaded[0].Host.Should().Be("pg.company.internal");
        loaded[1].Name.Should().Be("Local Analytics");
        loaded[1].FilePath.Should().Be("/tmp/analytics.db");
    }

    [Fact]
    public void SaveEmptyConnections_And_LoadConnections_ReturnsEmptyListInsteadOfDefaults()
    {
        ConnectionStorageService.SaveConnections(new List<ConnectionConfig>(), _tempFile);
        File.Exists(_tempFile).Should().BeTrue();

        var loaded = ConnectionStorageService.LoadConnections(_tempFile);

        loaded.Should().NotBeNull();
        loaded.Should().BeEmpty();
    }

    [Fact]
    public void SetLastUsedConnectionId_And_GetLastUsedConnectionId_RoundTripsCorrectly()
    {
        var tempLastUsed = Path.Combine(Path.GetTempPath(), $"runix_last_conn_{Guid.NewGuid():N}.txt");
        try
        {
            var initial = ConnectionStorageService.GetLastUsedConnectionId(tempLastUsed);
            initial.Should().BeNull();

            ConnectionStorageService.SetLastUsedConnectionId("conn-12345", tempLastUsed);
            var result = ConnectionStorageService.GetLastUsedConnectionId(tempLastUsed);

            result.Should().Be("conn-12345");
        }
        finally
        {
            if (File.Exists(tempLastUsed))
            {
                try { File.Delete(tempLastUsed); } catch { }
            }
        }
    }
}
