using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Infrastructure.Security;
using Xunit;

namespace Runix.Infrastructure.Tests.Security;

public class ConnectionBundleServiceTests
{
    [Fact]
    public void EncryptAndDecryptBundle_RoundTrip_PreservesAllConnectionsQueriesAndSnippets()
    {
        var service = new ConnectionBundleService();
        var bundle = new RunixConnectionBundle
        {
            FormatVersion = "rcf-v1",
            ExportedBy = "dev-workstation",
            Connections = new List<ConnectionConfig>
            {
                new()
                {
                    Id = "conn-1",
                    Name = "Produção PostgreSQL",
                    Type = DatabaseType.PostgreSQL,
                    Host = "db.prod.internal",
                    Port = 5432,
                    Database = "billing",
                    Username = "admin",
                    Password = "SuperSecretDbPassword!2026",
                    UseSshTunnel = true,
                    SshHost = "bastion.prod.internal",
                    SshPort = 22,
                    SshUsername = "ops",
                    SshAuthMethod = SshAuthMethod.PrivateKey,
                    SshPrivateKeyPath = "~/.ssh/id_ed25519",
                    SshPrivateKeyPassphrase = "SshKeyPassphrase#99"
                }
            },
            SavedQueries = new List<SavedQuery>
            {
                new()
                {
                    Id = "q-1",
                    Title = "Relatório de Faturamento Mensal",
                    SqlText = "SELECT date_trunc('month', created_at), SUM(total) FROM orders GROUP BY 1;",
                    Tags = new List<string> { "financeiro", "mensal" },
                    IsFavorite = true
                }
            },
            CustomSnippets = new List<CustomSnippet>
            {
                new()
                {
                    Id = "snip-1",
                    Prefix = "seltop",
                    Title = "Select Top 100",
                    Description = "Retorna as primeiras 100 linhas",
                    Body = "SELECT * FROM ${1:table_name} LIMIT 100;"
                }
            }
        };

        var encryptedBytes = service.EncryptBundle(bundle, "SenhaForteCompartilhada!123");
        Assert.NotNull(encryptedBytes);
        Assert.True(encryptedBytes.Length > 48);
        // Header magic bytes 'R', 'C', 'F', '1'
        Assert.Equal((byte)'R', encryptedBytes[0]);
        Assert.Equal((byte)'C', encryptedBytes[1]);
        Assert.Equal((byte)'F', encryptedBytes[2]);
        Assert.Equal((byte)'1', encryptedBytes[3]);

        var decrypted = service.DecryptBundle(encryptedBytes, "SenhaForteCompartilhada!123");

        Assert.Single(decrypted.Connections);
        Assert.Equal("Produção PostgreSQL", decrypted.Connections[0].Name);
        Assert.Equal("SuperSecretDbPassword!2026", decrypted.Connections[0].Password);
        Assert.True(decrypted.Connections[0].UseSshTunnel);
        Assert.Equal("SshKeyPassphrase#99", decrypted.Connections[0].SshPrivateKeyPassphrase);

        Assert.Single(decrypted.SavedQueries);
        Assert.Equal("Relatório de Faturamento Mensal", decrypted.SavedQueries[0].Title);
        Assert.Contains("financeiro", decrypted.SavedQueries[0].Tags);

        Assert.Single(decrypted.CustomSnippets);
        Assert.Equal("seltop", decrypted.CustomSnippets[0].Prefix);
    }

    [Fact]
    public void DecryptBundle_WithWrongPassword_ThrowsCryptographicException()
    {
        var service = new ConnectionBundleService();
        var bundle = new RunixConnectionBundle
        {
            Connections = new List<ConnectionConfig>
            {
                new() { Name = "Staging DB", Password = "secret" }
            }
        };

        var encrypted = service.EncryptBundle(bundle, "CorrectPassword123");

        Assert.ThrowsAny<CryptographicException>(() =>
            service.DecryptBundle(encrypted, "WrongPassword999"));
    }

    [Fact]
    public async Task ExportToFileAndReadAndDecryptFileAsync_RoundTripFile_WorksCorrectly()
    {
        var service = new ConnectionBundleService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"runix_test_{Guid.NewGuid():N}.rcf");

        try
        {
            var bundle = new RunixConnectionBundle
            {
                Connections = new List<ConnectionConfig>
                {
                    new() { Name = "SQLite Local", Type = DatabaseType.SQLite, FilePath = "demo.db" }
                }
            };

            await service.ExportToFileAsync(tempFile, bundle, "MasterPass#2026");
            Assert.True(File.Exists(tempFile));

            var restored = await service.ReadAndDecryptFileAsync(tempFile, "MasterPass#2026");
            Assert.Single(restored.Connections);
            Assert.Equal("SQLite Local", restored.Connections[0].Name);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
