using System;
using System.IO;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Security;
using Xunit;

namespace Runix.Infrastructure.Tests.Security;

public class SshTunnelServiceTests
{
    [Fact]
    public async Task EstablishTunnelAsync_AllocatesDistinctEphemeralLoopbackPortAndCleansUpOnDispose()
    {
        await using var tunnel1 = new SshTunnelService(useLoopbackSimulation: true);
        await using var tunnel2 = new SshTunnelService(useLoopbackSimulation: true);

        var cfg = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "10.0.0.15",
            Port = 5432,
            UseSshTunnel = true,
            SshHost = "bastion.example.com",
            SshPort = 22,
            SshUsername = "deploy"
        };

        var port1 = await tunnel1.EstablishTunnelAsync(cfg);
        var port2 = await tunnel2.EstablishTunnelAsync(cfg);

        Assert.True(tunnel1.IsActive);
        Assert.True(tunnel2.IsActive);
        Assert.True(port1 > 1024);
        Assert.True(port2 > 1024);
        Assert.NotEqual(port1, port2);

        await tunnel1.CloseTunnelAsync();
        Assert.False(tunnel1.IsActive);
        Assert.Equal(0, tunnel1.LocalBoundPort);
    }

    [Fact]
    public void ExpandTildePath_ExpandsUserProfileCorrectly()
    {
        var input = "~/.ssh/id_ed25519";
        var expanded = SshTunnelService.ExpandTildePath(input);

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(userHome, expanded);
        Assert.EndsWith(Path.Combine(".ssh", "id_ed25519"), expanded);
    }

    [Fact]
    public async Task EstablishTunnelAsync_DisabledSshTunnel_ReturnsOriginalPortWithoutStartingTunnel()
    {
        await using var tunnel = new SshTunnelService();
        var cfg = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "localhost",
            Port = 5433,
            UseSshTunnel = false
        };

        var port = await tunnel.EstablishTunnelAsync(cfg);

        Assert.False(tunnel.IsActive);
        Assert.Equal(5433, port);
    }

    [Fact]
    public async Task EstablishTunnelAsync_MissingSshHost_ThrowsInvalidOperationException()
    {
        await using var tunnel = new SshTunnelService();
        var cfg = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "10.0.0.5",
            UseSshTunnel = true,
            SshHost = ""
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => tunnel.EstablishTunnelAsync(cfg));
        Assert.Contains("host do servidor SSH", ex.Message);
    }

    [Fact]
    public async Task EstablishTunnelAsync_PrivateKeyNotFound_ThrowsFileNotFoundException()
    {
        await using var tunnel = new SshTunnelService();
        var cfg = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "10.0.0.5",
            UseSshTunnel = true,
            SshHost = "bastion.cloud.internal",
            SshAuthMethod = SshAuthMethod.PrivateKey,
            SshPrivateKeyPath = "/tmp/non_existent_key_12345.pem"
        };

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => tunnel.EstablishTunnelAsync(cfg));
        Assert.Contains("não foi encontrado", ex.Message);
    }

    [Fact]
    public void ConnectionConfig_GetSessionKey_IncludesSshBastionHost()
    {
        var cfgWithoutSsh = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "internal-db.rds.amazonaws.com",
            Port = 5432,
            Database = "mydb",
            Username = "dbadmin",
            UseSshTunnel = false
        };

        var cfgWithSsh = new ConnectionConfig
        {
            Type = DatabaseType.PostgreSQL,
            Host = "internal-db.rds.amazonaws.com",
            Port = 5432,
            Database = "mydb",
            Username = "dbadmin",
            UseSshTunnel = true,
            SshHost = "bastion.mycorp.com"
        };

        Assert.Equal("postgres:internal-db.rds.amazonaws.com:5432:mydb:dbadmin", cfgWithoutSsh.GetSessionKey());
        Assert.Equal("postgres:ssh:bastion.mycorp.com:internal-db.rds.amazonaws.com:5432:mydb:dbadmin", cfgWithSsh.GetSessionKey());
    }
}
