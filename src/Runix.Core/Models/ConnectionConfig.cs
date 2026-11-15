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
            SshPrivateKeyPassphrase = SshPrivateKeyPassphrase
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
