using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Security;

namespace Runix.Infrastructure.Storage;

public class ConnectionStorageService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RUNIX");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDir, "connections.json");
    private static readonly string LastUsedFilePath = Path.Combine(ConfigDir, "last_used_connection.txt");

    public static string? GetLastUsedConnectionId(string? filePath = null)
    {
        var targetFile = filePath ?? LastUsedFilePath;
        try
        {
            if (File.Exists(targetFile))
            {
                var id = File.ReadAllText(targetFile).Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    return id;
                }
            }
        }
        catch
        {
            // Ignore error
        }
        return null;
    }

    public static void SetLastUsedConnectionId(string connectionId, string? filePath = null)
    {
        var targetFile = filePath ?? LastUsedFilePath;
        try
        {
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            File.WriteAllText(targetFile, connectionId.Trim());
        }
        catch
        {
            // Ignore error
        }
    }

    public static List<ConnectionConfig> LoadConnections(string? filePath = null, ICredentialVaultService? vault = null)
    {
        var targetFile = filePath ?? ConfigFilePath;
        var credentialVault = vault ?? AesCredentialVaultService.Instance;
        try
        {
            if (File.Exists(targetFile))
            {
                var json = File.ReadAllText(targetFile);
                var items = JsonSerializer.Deserialize<List<ConnectionConfig>>(json);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        item.Password = credentialVault.Unprotect(item.Password);
                        item.SshPassword = credentialVault.Unprotect(item.SshPassword);
                        item.SshPrivateKeyPassphrase = credentialVault.Unprotect(item.SshPrivateKeyPassphrase);
                    }
                    return items;
                }
            }
        }
        catch
        {
            // Fallback to default
        }

        return GetDefaultConnections();
    }

    public static void SaveConnections(List<ConnectionConfig> connections, string? filePath = null, ICredentialVaultService? vault = null)
    {
        var targetFile = filePath ?? ConfigFilePath;
        var credentialVault = vault ?? AesCredentialVaultService.Instance;
        try
        {
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Clone configs before encrypting so live in-memory instances remain decrypted
            var encryptedCopies = connections.Select(c =>
            {
                var clone = c.Clone();
                clone.Password = credentialVault.Protect(clone.Password);
                clone.SshPassword = credentialVault.Protect(clone.SshPassword);
                clone.SshPrivateKeyPassphrase = credentialVault.Protect(clone.SshPrivateKeyPassphrase);
                return clone;
            }).ToList();

            var json = JsonSerializer.Serialize(encryptedCopies, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetFile, json);
        }
        catch
        {
            // Ignore error
        }
    }

    public static List<ConnectionConfig> GetDefaultConnections()
    {
        // Check for local demo.db in app base directory or current directory
        var demoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demo.db");
        if (!File.Exists(demoPath))
        {
            demoPath = Path.GetFullPath("demo.db");
        }

        return new List<ConnectionConfig>
        {
            new ConnectionConfig
            {
                Id = "demo-sqlite",
                Name = "Cheeses Demo (SQLite)",
                Type = DatabaseType.SQLite,
                FilePath = demoPath
            },
            new ConnectionConfig
            {
                Id = "local-postgres",
                Name = "Local PostgreSQL",
                Type = DatabaseType.PostgreSQL,
                Host = "localhost",
                Port = 5432,
                Database = "postgres",
                Username = "postgres",
                Password = "",
                SslMode = "Prefer"
            }
        };
    }
}
