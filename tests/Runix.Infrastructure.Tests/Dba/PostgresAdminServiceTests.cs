using System;
using System.Collections.Generic;
using Runix.Core.Models.Dba;
using Runix.Infrastructure.Dba;
using Xunit;

namespace Runix.Infrastructure.Tests.Dba;

public class PostgresAdminServiceTests
{
    private readonly PostgresAdminService _service = PostgresAdminService.Instance;

    [Fact]
    public void GenerateCreateRoleDdl_ProducesValidSyntax()
    {
        var role = new PostgresRoleInfo
        {
            RoleName = "analyst",
            CanLogin = true,
            IsSuperuser = false,
            CanCreateDb = true,
            CanCreateRole = false,
            InheritsPrivileges = true,
            IsReplication = false,
            ConnectionLimit = 10,
            ValidUntil = new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };

        var ddl = _service.GenerateCreateRoleDdl(role, "pass'word");

        Assert.Contains("CREATE ROLE \"analyst\" WITH", ddl);
        Assert.Contains("LOGIN", ddl);
        Assert.Contains("NOSUPERUSER", ddl);
        Assert.Contains("CREATEDB", ddl);
        Assert.Contains("NOCREATEROLE", ddl);
        Assert.Contains("INHERIT", ddl);
        Assert.Contains("NOREPLICATION", ddl);
        Assert.Contains("CONNECTION LIMIT 10", ddl);
        Assert.Contains("PASSWORD 'pass''word'", ddl);
        Assert.Contains("VALID UNTIL '2027-12-31 23:59:59'", ddl);
        Assert.EndsWith(";", ddl);
    }

    [Fact]
    public void GenerateAlterRoleDdl_WithInfinityAndFlags()
    {
        var role = new PostgresRoleInfo
        {
            RoleName = "reporting_user",
            CanLogin = true,
            IsSuperuser = true,
            ValidUntil = null // No expiration
        };

        var ddl = _service.GenerateAlterRoleDdl(role, "new_secret");

        Assert.Contains("ALTER ROLE \"reporting_user\" WITH", ddl);
        Assert.Contains("SUPERUSER", ddl);
        Assert.Contains("PASSWORD 'new_secret'", ddl);
        Assert.Contains("VALID UNTIL 'infinity'", ddl);
    }

    [Fact]
    public void GenerateDropRoleDdl_EscapesIdentifier()
    {
        var ddl = _service.GenerateDropRoleDdl("complex \"hacker\" role");
        Assert.Equal("DROP ROLE \"complex \"\"hacker\"\" role\";", ddl);
    }

    [Fact]
    public void GeneratePrivilegeDdl_GroupsGrantsAndRevokes()
    {
        var changes = new List<PostgresPrivilegeChange>
        {
            new() { SchemaName = "public", TableName = "orders", RoleName = "app_user", Privilege = "SELECT", IsGrant = true },
            new() { SchemaName = "public", TableName = "orders", RoleName = "app_user", Privilege = "INSERT", IsGrant = true },
            new() { SchemaName = "public", TableName = "orders", RoleName = "app_user", Privilege = "DELETE", IsGrant = false },
            new() { SchemaName = "public", TableName = "customers", RoleName = "app_user", Privilege = "SELECT", IsGrant = true }
        };

        var ddl = _service.GeneratePrivilegeDdl(changes);

        Assert.Contains("GRANT SELECT, INSERT ON \"public\".\"orders\" TO \"app_user\";", ddl);
        Assert.Contains("REVOKE DELETE ON \"public\".\"orders\" FROM \"app_user\";", ddl);
        Assert.Contains("GRANT SELECT ON \"public\".\"customers\" TO \"app_user\";", ddl);
    }

    [Fact]
    public void ActiveSessions_CalculatesDurationsAndBadges()
    {
        var normalSession = new PostgresActiveSession
        {
            Pid = 101,
            State = "active",
            ElapsedSeconds = 2.4
        };

        var slowSession = new PostgresActiveSession
        {
            Pid = 102,
            State = "active",
            ElapsedSeconds = 12.0
        };

        var criticalSession = new PostgresActiveSession
        {
            Pid = 103,
            State = "active",
            ElapsedSeconds = 65.5
        };

        var idleSession = new PostgresActiveSession
        {
            Pid = 104,
            State = "idle",
            ElapsedSeconds = 120.0
        };

        Assert.False(normalSession.IsSlowQuery);
        Assert.False(normalSession.IsCriticalSlowQuery);
        Assert.Equal("2.4s", normalSession.DurationFormatted);

        Assert.True(slowSession.IsSlowQuery);
        Assert.False(slowSession.IsCriticalSlowQuery);
        Assert.Equal("12.0s", slowSession.DurationFormatted);

        Assert.True(criticalSession.IsSlowQuery);
        Assert.True(criticalSession.IsCriticalSlowQuery);
        Assert.Equal("1m 05s", criticalSession.DurationFormatted);

        // Idle session is not considered a slow query even with high elapsed duration
        Assert.False(idleSession.IsSlowQuery);
        Assert.False(idleSession.IsCriticalSlowQuery);
    }
}
