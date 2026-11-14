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
}
