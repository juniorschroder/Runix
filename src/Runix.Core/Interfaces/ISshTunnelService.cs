using System;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface ISshTunnelService : IAsyncDisposable, IDisposable
{
    bool IsActive { get; }
    int LocalBoundPort { get; }

    Task<int> EstablishTunnelAsync(ConnectionConfig config, CancellationToken cancellationToken = default);
    Task CloseTunnelAsync();
}
