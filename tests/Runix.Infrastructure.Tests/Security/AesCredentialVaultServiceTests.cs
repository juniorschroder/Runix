using System;
using System.Collections.Generic;
using System.IO;
using Runix.Core.Models;
using Runix.Infrastructure.Security;
using Runix.Infrastructure.Storage;
using Xunit;

namespace Runix.Infrastructure.Tests.Security;

public class AesCredentialVaultServiceTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsSecretWithAes256GcmEnvelope()
    {
        var vault = new AesCredentialVaultService("unit-test-master-key");
        var plain = "Super$ecretP@ssw0rd!2026";

        var encrypted = vault.Protect(plain);
        Assert.True(vault.IsEncrypted(encrypted));
        Assert.StartsWith(AesCredentialVaultService.EnvelopePrefix, encrypted);
        Assert.NotEqual(plain, encrypted);

        // Calling Protect again on already encrypted text does not double-encrypt
        Assert.Equal(encrypted, vault.Protect(encrypted));

        var decrypted = vault.Unprotect(encrypted);
        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Unprotect_PreservesLegacyPlaintextPasswordsBackwardCompatibly()
    {
        var vault = new AesCredentialVaultService("unit-test-master-key");
        var legacyPlain = "legacy_plain_password";

        Assert.False(vault.IsEncrypted(legacyPlain));
        Assert.Equal(legacyPlain, vault.Unprotect(legacyPlain));
    }

    [Fact]
    public void ConnectionStorageService_EncryptsOnDiskAndDecryptsInMemoryWithoutMutatingLiveInstances()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"runix_conn_vault_{Guid.NewGuid():N}.json");
        try
        {
            var cfg = new ConnectionConfig
            {
                Id = "pg-prod",
                Name = "Production PG",
                Type = DatabaseType.PostgreSQL,
                Password = "db-secret-password",
                UseSshTunnel = true,
                SshHost = "bastion.internal",
                SshPassword = "ssh-secret-password",
                SshPrivateKeyPassphrase = "key-secret-passphrase"
            };

            ConnectionStorageService.SaveConnections(new List<ConnectionConfig> { cfg }, tempFile);

            // Live instance must remain plaintext in memory
            Assert.Equal("db-secret-password", cfg.Password);
            Assert.Equal("ssh-secret-password", cfg.SshPassword);

            // Raw JSON on disk must contain encrypted enc:v1: envelopes and NEVER the raw password
            var rawJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("db-secret-password", rawJson);
            Assert.DoesNotContain("ssh-secret-password", rawJson);
            Assert.Contains("enc:v1:", rawJson);

            // Loading from disk must transparently decrypt all secrets
            var loaded = ConnectionStorageService.LoadConnections(tempFile);
            Assert.Single(loaded);
            Assert.Equal("db-secret-password", loaded[0].Password);
            Assert.Equal("ssh-secret-password", loaded[0].SshPassword);
            Assert.Equal("key-secret-passphrase", loaded[0].SshPrivateKeyPassphrase);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
