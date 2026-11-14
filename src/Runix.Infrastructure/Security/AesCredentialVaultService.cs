using System;
using System.Security.Cryptography;
using System.Text;
using Runix.Core.Interfaces;

namespace Runix.Infrastructure.Security;

public class AesCredentialVaultService : ICredentialVaultService
{
    public const string EnvelopePrefix = "enc:v1:";
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    private const int Pbkdf2Iterations = 50_000;

    private readonly string _masterSecret;

    public static AesCredentialVaultService Instance { get; } = new();

    public AesCredentialVaultService(string? customMasterSecret = null)
    {
        _masterSecret = string.IsNullOrWhiteSpace(customMasterSecret)
            ? $"RUNIX-VAULT-KEY-v4::{Environment.UserName}::{Environment.MachineName}"
            : customMasterSecret;
    }

    public bool IsEncrypted(string? text)
    {
        return !string.IsNullOrEmpty(text) &&
               text.StartsWith(EnvelopePrefix, StringComparison.Ordinal);
    }

    public string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        // Prevent double encryption if already protected
        if (IsEncrypted(plainText))
            return plainText;

        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        Span<byte> key = stackalloc byte[KeySize];
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(_masterSecret),
            salt,
            key,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var combined = new byte[SaltSize + NonceSize + TagSize + cipherBytes.Length];
        salt.CopyTo(combined.AsSpan(0, SaltSize));
        nonce.CopyTo(combined.AsSpan(SaltSize, NonceSize));
        tag.CopyTo(combined.AsSpan(SaltSize + NonceSize, TagSize));
        cipherBytes.CopyTo(combined.AsSpan(SaltSize + NonceSize + TagSize));

        return EnvelopePrefix + Convert.ToBase64String(combined);
    }

    public string Unprotect(string? storedText)
    {
        if (string.IsNullOrEmpty(storedText))
            return string.Empty;

        // Legacy plaintext backward compatibility
        if (!IsEncrypted(storedText))
            return storedText;

        try
        {
            var base64 = storedText[EnvelopePrefix.Length..];
            var combined = Convert.FromBase64String(base64);
            if (combined.Length < SaltSize + NonceSize + TagSize)
                return string.Empty;

            var salt = combined.AsSpan(0, SaltSize);
            var nonce = combined.AsSpan(SaltSize, NonceSize);
            var tag = combined.AsSpan(SaltSize + NonceSize, TagSize);
            var cipherBytes = combined.AsSpan(SaltSize + NonceSize + TagSize);

            Span<byte> key = stackalloc byte[KeySize];
            Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(_masterSecret),
                salt,
                key,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256);

            var plainBytes = new byte[cipherBytes.Length];
            using (var aesGcm = new AesGcm(key, TagSize))
            {
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
