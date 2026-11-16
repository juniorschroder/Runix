using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Runix.Core.Models;

public enum SshAuthMethod
{
    Password,
    PrivateKey
}

public class ConnectionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Connection";
    public string Category { get; set; } = string.Empty;
    public DatabaseType Type { get; set; } = DatabaseType.SQLite;
    
    // SQLite specific
    public string FilePath { get; set; } = string.Empty;

    // PostgreSQL specific
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = string.Empty;
    public string SslMode { get; set; } = "Prefer";

    // SSH Tunnel Configuration
    public bool UseSshTunnel { get; set; }
    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = string.Empty;
    public SshAuthMethod SshAuthMethod { get; set; } = SshAuthMethod.Password;
    public string SshPassword { get; set; } = string.Empty;
    public string SshPrivateKeyPath { get; set; } = string.Empty;
    public string SshPrivateKeyPassphrase { get; set; } = string.Empty;

    // Safety, Read-Only & Production Safeguards (v1.2.0)
    public bool IsReadOnly { get; set; }
    public bool IsProduction { get; set; }
    public bool SafeExecutionMode { get; set; } = true;

    public bool IsProductionEnvironment =>
        IsProduction ||
        (!string.IsNullOrWhiteSpace(Category) && IsProductionKeyword(Category)) ||
        (!string.IsNullOrWhiteSpace(Name) && IsProductionKeyword(Name));

    private static bool IsProductionKeyword(string text) =>
        text.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("produção", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("production", StringComparison.OrdinalIgnoreCase);

    public string DisplaySubtitle => Type switch
    {
        DatabaseType.SQLite => string.IsNullOrWhiteSpace(FilePath) ? "No file selected" : FilePath,
        DatabaseType.PostgreSQL => UseSshTunnel && !string.IsNullOrWhiteSpace(SshHost)
            ? $"🔒 SSH ({SshHost}) → {Username}@{Host}:{Port}/{Database}"
            : $"{Username}@{Host}:{Port}/{Database}",
        _ => ""
    };

    public string EngineIcon => Type switch
    {
        DatabaseType.SQLite => "📁",
        DatabaseType.PostgreSQL => "🐘",
        _ => "🗄"
    };

    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);

    public string GetSessionKey()
    {
        if (Type == DatabaseType.SQLite)
        {
            var path = !string.IsNullOrWhiteSpace(FilePath) ? FilePath.Trim().ToLowerInvariant() : Id;
            return $"sqlite:{path}";
        }

        var host = !string.IsNullOrWhiteSpace(Host) ? Host.Trim().ToLowerInvariant() : "localhost";
        var db = !string.IsNullOrWhiteSpace(Database) ? Database.Trim().ToLowerInvariant() : "postgres";
        var user = !string.IsNullOrWhiteSpace(Username) ? Username.Trim().ToLowerInvariant() : "postgres";

        if (UseSshTunnel)
        {
            var sshHost = !string.IsNullOrWhiteSpace(SshHost) ? SshHost.Trim().ToLowerInvariant() : "bastion";
            return $"postgres:ssh:{sshHost}:{host}:{Port}:{db}:{user}";
        }

        return $"postgres:{host}:{Port}:{db}:{user}";
    }

    public ConnectionConfig Clone()
    {
        return new ConnectionConfig
        {
            Id = Id,
            Name = Name,
            Category = Category,
            Type = Type,
            FilePath = FilePath,
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            SslMode = SslMode,
            UseSshTunnel = UseSshTunnel,
            SshHost = SshHost,
            SshPort = SshPort,
            SshUsername = SshUsername,
            SshAuthMethod = SshAuthMethod,
            SshPassword = SshPassword,
            SshPrivateKeyPath = SshPrivateKeyPath,
            SshPrivateKeyPassphrase = SshPrivateKeyPassphrase,
            IsReadOnly = IsReadOnly,
            IsProduction = IsProduction,
            SafeExecutionMode = SafeExecutionMode
        };
    }

    public override string ToString() => $"{Name} ({Type})";
}

public class ConnectionCategoryGroup : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public string CategoryName { get; set; } = string.Empty;
    public string DisplayTitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "📂";
    public bool IsUncategorized { get; set; }
    public ObservableCollection<ConnectionConfig> Connections { get; } = new();

    public int Count => Connections.Count;
    public string HeaderBadge => Count.ToString();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
