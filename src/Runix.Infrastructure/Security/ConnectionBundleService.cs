using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Security;

public sealed class ConnectionBundleService : IConnectionBundleService
{
    private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("RCF1");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    private const int Pbkdf2Iterations = 100_000;

    public static ConnectionBundleService Instance { get; } = new();

    public byte[] EncryptBundle(RunixConnectionBundle bundle, string password)
    {
        if (bundle == null)
        {
            throw new ArgumentNullException(nameof(bundle));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("A senha de proteção do arquivo .rcf não pode ser vazia.", nameof(password));
        }

        byte[] plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(bundle, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        byte[] key = DeriveKey(password, salt);

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag, MagicHeader);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        // Layout: [4B "RCF1"] [16B Salt] [12B Nonce] [16B Tag] [Ciphertext...]
        byte[] output = new byte[MagicHeader.Length + SaltSize + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;

        Buffer.BlockCopy(MagicHeader, 0, output, offset, MagicHeader.Length);
        offset += MagicHeader.Length;

        Buffer.BlockCopy(salt, 0, output, offset, SaltSize);
        offset += SaltSize;

        Buffer.BlockCopy(nonce, 0, output, offset, NonceSize);
        offset += NonceSize;

        Buffer.BlockCopy(tag, 0, output, offset, TagSize);
        offset += TagSize;

        Buffer.BlockCopy(ciphertext, 0, output, offset, ciphertext.Length);
        return output;
    }

    public RunixConnectionBundle DecryptBundle(byte[] rcfBytes, string password)
    {
        if (rcfBytes == null || rcfBytes.Length < MagicHeader.Length + SaltSize + NonceSize + TagSize)
        {
            throw new InvalidDataException("O arquivo selecionado não é um arquivo .rcf válido do RUNIX.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Informe a senha para desbloquear o arquivo .rcf.", nameof(password));
        }

        for (int i = 0; i < MagicHeader.Length; i++)
        {
            if (rcfBytes[i] != MagicHeader[i])
            {
                throw new InvalidDataException("Cabeçalho inválido: o arquivo não está no formato Runix Connections File (.rcf).");
            }
        }

        int offset = MagicHeader.Length;
        byte[] salt = new byte[SaltSize];
        Buffer.BlockCopy(rcfBytes, offset, salt, 0, SaltSize);
        offset += SaltSize;

        byte[] nonce = new byte[NonceSize];
        Buffer.BlockCopy(rcfBytes, offset, nonce, 0, NonceSize);
        offset += NonceSize;

        byte[] tag = new byte[TagSize];
        Buffer.BlockCopy(rcfBytes, offset, tag, 0, TagSize);
        offset += TagSize;

        int cipherLen = rcfBytes.Length - offset;
        byte[] ciphertext = new byte[cipherLen];
        Buffer.BlockCopy(rcfBytes, offset, ciphertext, 0, cipherLen);

        byte[] plaintext = new byte[cipherLen];
        byte[] key = DeriveKey(password, salt);

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, MagicHeader);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Senha incorreta ou arquivo .rcf adulterado/corrompido.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var bundle = JsonSerializer.Deserialize<RunixConnectionBundle>(plaintext);
        return bundle ?? throw new InvalidDataException("Conteúdo do arquivo .rcf está vazio ou inválido.");
    }

    public async Task ExportToFileAsync(
        string filePath,
        RunixConnectionBundle bundle,
        string password,
        CancellationToken cancellationToken = default)
    {
        byte[] encrypted = EncryptBundle(bundle, password);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(filePath, encrypted, cancellationToken);
    }

    public async Task<RunixConnectionBundle> ReadAndDecryptFileAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arquivo .rcf não encontrado.", filePath);
        }

        byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return DecryptBundle(bytes, password);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }
}
