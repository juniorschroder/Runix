using System;
using FluentAssertions;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class ConnectionConfigTests
{
    [Fact]
    public void Constructor_DefaultProperties_InitializesValidDefaults()
    {
        var config = new ConnectionConfig();

        config.Id.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(config.Id, out _).Should().BeTrue("Default Id should be a valid GUID");
        config.Name.Should().Be("New Connection");
        config.Type.Should().Be(DatabaseType.SQLite);
        config.Port.Should().Be(5432);
        config.Host.Should().Be("localhost");
        config.Database.Should().Be("postgres");
        config.Username.Should().Be("postgres");
        config.SslMode.Should().Be("Prefer");
    }

    [Fact]
    public void DisplaySubtitle_SQLite_WithEmptyFilePath_ReturnsNoFileSelected()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = ""
        };

        config.DisplaySubtitle.Should().Be("No file selected");
    }

    [Fact]
    public void DisplaySubtitle_SQLite_WithFilePath_ReturnsFilePath()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.SQLite,
            FilePath = "/path/to/my_database.db"
        };

        config.DisplaySubtitle.Should().Be("/path/to/my_database.db");
    }

    [Fact]
    public void DisplaySubtitle_PostgreSQL_ReturnsFormattedConnectionTarget()
    {
        var config = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "db.internal.corp",
            Port = 5433,
            Database = "production",
            Username = "admin_user"
        };

        config.DisplaySubtitle.Should().Be("admin_user@db.internal.corp:5433/production");
    }

    [Fact]
    public void EngineIcon_ReturnsMatchingEmojiIcon()
    {
        var sqlite = new ConnectionConfig { Type = DatabaseType.SQLite };
        sqlite.EngineIcon.Should().Be("📁");

        var postgres = new ConnectionConfig { Type = DatabaseType.PostgreSQL };
        postgres.EngineIcon.Should().Be("🐘");
    }

    [Fact]
    public void ToString_ReturnsNameAndType()
    {
        var config = new ConnectionConfig
        {
            Name = "Analytics Store",
            Type = DatabaseType.PostgreSQL
        };

        config.ToString().Should().Be("Analytics Store (PostgreSQL)");
    }
}
