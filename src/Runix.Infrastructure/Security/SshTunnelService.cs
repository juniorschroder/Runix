using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Security;

public class SshTunnelService : ISshTunnelService
{
    private Process? _sshProcess;
    private TcpListener? _loopbackStubListener;
    private readonly bool _useLoopbackSimulation;

    public bool IsActive { get; private set; }
    public int LocalBoundPort { get; private set; }

    public SshTunnelService(bool useLoopbackSimulation = false)
    {
        _useLoopbackSimulation = useLoopbackSimulation;
    }

    public static int AllocateEphemeralLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task<int> EstablishTunnelAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        await CloseTunnelAsync();

        if (!config.UseSshTunnel)
        {
            LocalBoundPort = config.Port > 0 ? config.Port : 5432;
            IsActive = false;
            return LocalBoundPort;
        }

        if (string.IsNullOrWhiteSpace(config.SshHost))
            throw new InvalidOperationException("O host do servidor SSH é obrigatório quando o Túnel SSH está habilitado.");

        if (_useLoopbackSimulation)
        {
            _loopbackStubListener = new TcpListener(IPAddress.Loopback, 0);
            _loopbackStubListener.Start();
            LocalBoundPort = ((IPEndPoint)_loopbackStubListener.LocalEndpoint).Port;
            IsActive = true;
            return LocalBoundPort;
        }

        LocalBoundPort = AllocateEphemeralLoopbackPort();
        var remoteHost = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
        var remotePort = config.Port > 0 ? config.Port : 5432;
        var sshPort = config.SshPort > 0 ? config.SshPort : 22;
        var sshUser = string.IsNullOrWhiteSpace(config.SshUsername) ? Environment.UserName : config.SshUsername;

        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        psi.ArgumentList.Add("-N");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("BatchMode=yes");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ExitOnForwardFailure=yes");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ConnectTimeout=8");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("StrictHostKeyChecking=accept-new");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(sshPort.ToString());

        if (config.SshAuthMethod == SshAuthMethod.PrivateKey && !string.IsNullOrWhiteSpace(config.SshPrivateKeyPath))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(config.SshPrivateKeyPath);
        }

        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add($"127.0.0.1:{LocalBoundPort}:{remoteHost}:{remotePort}");
        psi.ArgumentList.Add($"{sshUser}@{config.SshHost}");

        _sshProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o cliente OpenSSH do sistema.");

        // Wait briefly to confirm process did not exit with immediate auth/network error
        await Task.Delay(250, cancellationToken);
        if (_sshProcess.HasExited)
        {
            var err = await _sshProcess.StandardError.ReadToEndAsync(cancellationToken);
            _sshProcess.Dispose();
            _sshProcess = null;
            IsActive = false;
            throw new InvalidOperationException($"Falha ao estabelecer Túnel SSH ({config.SshHost}:{sshPort}): {err.Trim()}");
        }

        IsActive = true;
        return LocalBoundPort;
    }

    public Task CloseTunnelAsync()
    {
        IsActive = false;
        LocalBoundPort = 0;

        if (_loopbackStubListener != null)
        {
            try { _loopbackStubListener.Stop(); } catch { }
            _loopbackStubListener = null;
        }

        if (_sshProcess != null)
        {
            try
            {
                if (!_sshProcess.HasExited)
                {
                    _sshProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill errors on already exited process
            }
            finally
            {
                _sshProcess.Dispose();
                _sshProcess = null;
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseTunnelAsync().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        CloseTunnelAsync().GetAwaiter().GetResult();
        return ValueTask.CompletedTask;
    }
}
