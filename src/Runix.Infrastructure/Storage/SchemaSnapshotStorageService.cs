using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models.Diff;
using Runix.Infrastructure.Diff.Exceptions;

namespace Runix.Infrastructure.Storage;

/// <summary>
/// Implementação do serviço de armazenamento, streaming com compressão GZip e verificação criptográfica SHA-256 para snapshots (.rschema).
/// </summary>
public class SchemaSnapshotStorageService : ISchemaSnapshotStorageService
{
    public static SchemaSnapshotStorageService Instance { get; } = new();

    public const byte GZipMagic1 = 0x1F;
    public const byte GZipMagic2 = 0x8B;
    public const long MaxAllowedFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    public static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly JsonSerializerOptions SerializationJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ComputeChecksum(SchemaSnapshotModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var prevCompromised = snapshot.IsIntegrityCompromised;
        try
        {
            snapshot.IsIntegrityCompromised = false;
            byte[] utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, CanonicalJsonOptions);
            byte[] hash = SHA256.HashData(utf8Bytes);
            return Convert.ToHexStringLower(hash);
        }
        finally
        {
            snapshot.IsIntegrityCompromised = prevCompromised;
        }
    }

    public bool VerifyChecksum(SchemaSnapshotEnvelope envelope, out string calculatedHash)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Snapshot);

        calculatedHash = ComputeChecksum(envelope.Snapshot);
        if (string.IsNullOrWhiteSpace(envelope.ChecksumSha256))
        {
            return false;
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(envelope.ChecksumSha256.Trim().ToLowerInvariant());
        byte[] actualBytes = Encoding.UTF8.GetBytes(calculatedHash.ToLowerInvariant());

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public async Task SaveSnapshotAsync(
        string filePath,
        SchemaSnapshotModel snapshot,
        SnapshotMetadata? metadata = null,
        bool compressGzip = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(snapshot);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        metadata ??= new SnapshotMetadata();
        if (string.IsNullOrEmpty(metadata.DatabaseName))
        {
            metadata.DatabaseName = snapshot.DatabaseName;
        }
        if (string.IsNullOrEmpty(metadata.EngineType))
        {
            metadata.EngineType = snapshot.EngineType;
        }
        if (metadata.TableCount == 0 && snapshot.Tables != null)
        {
            metadata.TableCount = snapshot.Tables.Count;
        }
        if (metadata.ViewCount == 0 && snapshot.Views != null)
        {
            metadata.ViewCount = snapshot.Views.Count;
        }
        if (metadata.TriggerCount == 0 && snapshot.Triggers != null)
        {
            metadata.TriggerCount = snapshot.Triggers.Count;
        }
        if (string.IsNullOrEmpty(metadata.Label))
        {
            metadata.Label = snapshot.Label;
        }
        if (string.IsNullOrEmpty(metadata.Notes))
        {
            metadata.Notes = snapshot.Notes;
        }

        string checksum = ComputeChecksum(snapshot);

        var envelope = new SchemaSnapshotEnvelope
        {
            FormatVersion = 1,
            AppVersion = AppConstants.AppVersion,
            ExportedAt = DateTime.UtcNow,
            ChecksumSha256 = checksum,
            Metadata = metadata,
            Snapshot = snapshot
        };

        string tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                BufferSize = 65536
            };

            await using (var fs = new FileStream(tempPath, fileOptions))
            {
                if (compressGzip)
                {
                    await using (var gz = new GZipStream(fs, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        await JsonSerializer.SerializeAsync(gz, envelope, SerializationJsonOptions, cancellationToken);
                        await gz.FlushAsync(cancellationToken);
                    }
                }
                else
                {
                    await JsonSerializer.SerializeAsync(fs, envelope, SerializationJsonOptions, cancellationToken);
                }

                await fs.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
            throw;
        }
    }

    public async Task<SchemaSnapshotEnvelope> LoadSnapshotAsync(
        string filePath,
        bool allowCompromised = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Snapshot file not found: {filePath}", filePath);
        }

        if (fileInfo.Length > MaxAllowedFileSizeBytes)
        {
            throw new InvalidOperationException($"Snapshot file exceeds maximum allowed size of {MaxAllowedFileSizeBytes / (1024 * 1024)} MB.");
        }

        var fileStreamOptions = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 65536,
            Options = FileOptions.Asynchronous
        };

        try
        {
            await using var fs = new FileStream(filePath, fileStreamOptions);
            return await LoadSnapshotAsync(fs, allowCompromised, cancellationToken);
        }
        catch (SnapshotIntegrityException ex) when (string.IsNullOrEmpty(ex.FilePath))
        {
            throw new SnapshotIntegrityException(filePath, ex.ExpectedHash, ex.ActualHash);
        }
    }

    public async Task<SchemaSnapshotEnvelope> LoadSnapshotAsync(
        Stream stream,
        bool allowCompromised = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream readStream = stream;
        bool isGzip = false;

        if (stream.CanSeek)
        {
            byte[] magic = new byte[2];
            int read = await stream.ReadAsync(magic.AsMemory(0, 2), cancellationToken);
            if (read == 2 && magic[0] == GZipMagic1 && magic[1] == GZipMagic2)
            {
                isGzip = true;
            }
            stream.Seek(0, SeekOrigin.Begin);
        }
        else
        {
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            readStream = ms;

            byte[] magic = new byte[2];
            int read = await readStream.ReadAsync(magic.AsMemory(0, 2), cancellationToken);
            if (read == 2 && magic[0] == GZipMagic1 && magic[1] == GZipMagic2)
            {
                isGzip = true;
            }
            readStream.Position = 0;
        }

        SchemaSnapshotEnvelope envelope;
        if (isGzip)
        {
            await using var gz = new GZipStream(readStream, CompressionMode.Decompress, leaveOpen: true);
            envelope = await DeserializeEnvelopeOrModelAsync(gz, cancellationToken);
        }
        else
        {
            envelope = await DeserializeEnvelopeOrModelAsync(readStream, cancellationToken);
        }

        bool isValid = VerifyChecksum(envelope, out string calculatedHash);
        if (!isValid)
        {
            if (!allowCompromised)
            {
                throw new SnapshotIntegrityException(envelope.ChecksumSha256, calculatedHash);
            }

            envelope.Snapshot.IsIntegrityCompromised = true;
        }

        return envelope;
    }

    private async Task<SchemaSnapshotEnvelope> DeserializeEnvelopeOrModelAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var doc = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("snapshot", out _) || root.TryGetProperty("Snapshot", out _))
        {
            var env = doc.Deserialize<SchemaSnapshotEnvelope>(SerializationJsonOptions);
            if (env != null && env.Snapshot != null)
            {
                return env;
            }
        }

        // Fallback for legacy or direct SchemaSnapshotModel payloads
        var model = doc.Deserialize<SchemaSnapshotModel>(SerializationJsonOptions);
        if (model != null)
        {
            string computedHash = ComputeChecksum(model);
            return new SchemaSnapshotEnvelope
            {
                FormatVersion = 1,
                AppVersion = AppConstants.AppVersion,
                ExportedAt = model.CreatedAt,
                ChecksumSha256 = computedHash,
                Metadata = new SnapshotMetadata
                {
                    DatabaseName = model.DatabaseName,
                    EngineType = model.EngineType,
                    TableCount = model.Tables?.Count ?? 0,
                    ViewCount = model.Views?.Count ?? 0,
                    TriggerCount = model.Triggers?.Count ?? 0,
                    Label = model.Label,
                    Notes = model.Notes
                },
                Snapshot = model
            };
        }

        throw new InvalidDataException("Unable to deserialize schema snapshot envelope or model.");
    }
}
