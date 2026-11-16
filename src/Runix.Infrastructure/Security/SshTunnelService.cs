using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Security;

public class SshTunnelService : ISshTunnelService
{
    private SshClient? _sshClient;
    private ForwardedPortLocal? _forwardedPort;
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

    public static string ExpandTildePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (path.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.TrimStart('~', '/', '\\'));
        }
        return Path.GetFullPath(path);
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
            throw new InvalidOperationException("O host do servidor SSH (Bastion / Jump Server) é obrigatório quando o Túnel SSH está habilitado.");

        if (_useLoopbackSimulation)
        {
            _loopbackStubListener = new TcpListener(IPAddress.Loopback, 0);
            _loopbackStubListener.Start();
            LocalBoundPort = ((IPEndPoint)_loopbackStubListener.LocalEndpoint).Port;
            IsActive = true;
            return LocalBoundPort;
        }

        var remoteHost = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
        var remotePort = config.Port > 0 ? config.Port : 5432;
        var sshPort = config.SshPort > 0 ? config.SshPort : 22;
        var sshUser = string.IsNullOrWhiteSpace(config.SshUsername) ? Environment.UserName : config.SshUsername;

        string? expandedKeyPath = null;
        if (config.SshAuthMethod == SshAuthMethod.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(config.SshPrivateKeyPath))
            {
                throw new InvalidOperationException("O caminho da chave privada SSH (.pem / id_ed25519) deve ser informado para autenticação por chave.");
            }

            expandedKeyPath = ExpandTildePath(config.SshPrivateKeyPath);
            if (!File.Exists(expandedKeyPath))
            {
                throw new FileNotFoundException($"O arquivo de chave privada SSH não foi encontrado em: '{expandedKeyPath}'", expandedKeyPath);
            }
        }

        // 1. Try native managed SSH.NET client first (pure C#, cross-platform, no external process)
        try
        {
            AuthenticationMethod authMethod;
            if (config.SshAuthMethod == SshAuthMethod.PrivateKey && !string.IsNullOrWhiteSpace(expandedKeyPath))
            {
                PrivateKeyFile keyFile;
                if (!string.IsNullOrWhiteSpace(config.SshPrivateKeyPassphrase))
                {
                    keyFile = new PrivateKeyFile(expandedKeyPath, config.SshPrivateKeyPassphrase);
                }
                else
                {
                    keyFile = new PrivateKeyFile(expandedKeyPath);
                }
                authMethod = new PrivateKeyAuthenticationMethod(sshUser, keyFile);
            }
            else
            {
                authMethod = new PasswordAuthenticationMethod(sshUser, config.SshPassword ?? string.Empty);
            }

            var connectionInfo = new ConnectionInfo(config.SshHost, sshPort, sshUser, authMethod)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            var client = new SshClient(connectionInfo);
            await Task.Run(() => client.Connect(), cancellationToken);

            var forwardedPort = new ForwardedPortLocal("127.0.0.1", 0, remoteHost, (uint)remotePort);
            client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();

            _sshClient = client;
            _forwardedPort = forwardedPort;
            LocalBoundPort = (int)forwardedPort.BoundPort;
            IsActive = true;
            return LocalBoundPort;
        }
        catch (Exception sshNetEx)
        {
            // Clean up any partially initialized managed client
            await CloseTunnelAsync();

            // 2. Fall back to system OpenSSH binary if available
            try
            {
                return await EstablishOpenSshProcessTunnelAsync(config, expandedKeyPath, remoteHost, remotePort, sshPort, sshUser, cancellationToken);
            }
            catch (Exception procEx)
            {
                throw new InvalidOperationException(
                    $"Falha ao estabelecer Túnel SSH com '{config.SshHost}:{sshPort}': {sshNetEx.Message}. (Fallback OpenSSH: {procEx.Message})",
                    sshNetEx);
            }
        }
    }

    private async Task<int> EstablishOpenSshProcessTunnelAsync(
        ConnectionConfig config,
        string? expandedKeyPath,
        string remoteHost,
        int remotePort,
        int sshPort,
        string sshUser,
        CancellationToken cancellationToken)
    {
        LocalBoundPort = AllocateEphemeralLoopbackPort();

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

        if (config.SshAuthMethod == SshAuthMethod.PrivateKey && !string.IsNullOrWhiteSpace(expandedKeyPath))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(expandedKeyPath);
        }

        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add($"127.0.0.1:{LocalBoundPort}:{remoteHost}:{remotePort}");
        psi.ArgumentList.Add($"{sshUser}@{config.SshHost}");

        _sshProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o cliente OpenSSH do sistema.");

        await Task.Delay(350, cancellationToken);
        if (_sshProcess.HasExited)
        {
            var err = await _sshProcess.StandardError.ReadToEndAsync(cancellationToken);
            _sshProcess.Dispose();
            _sshProcess = null;
            IsActive = false;
            throw new InvalidOperationException($"Falha OpenSSH ({config.SshHost}:{sshPort}): {err.Trim()}");
        }

        IsActive = true;
        return LocalBoundPort;
    }

    public Task CloseTunnelAsync()
    {
        IsActive = false;
        LocalBoundPort = 0;

        if (_forwardedPort != null)
        {
            try { _forwardedPort.Stop(); } catch { }
            _forwardedPort = null;
        }

        if (_sshClient != null)
        {
            try
            {
                if (_sshClient.IsConnected) _sshClient.Disconnect();
                _sshClient.Dispose();
            }
            catch { }
            _sshClient = null;
        }

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
            catch { }
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
