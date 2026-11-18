using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Constants;
using Runix.Core.Interfaces;
using Runix.Core.Models.Diff;

namespace Runix.Infrastructure.Storage;

/// <summary>
/// Implementação do serviço de gerenciamento do catálogo local de snapshots (~/.runix/snapshots/).
/// </summary>
public class SchemaSnapshotCatalogService : ISchemaSnapshotCatalogService
{
    private readonly ISchemaSnapshotStorageService _storageService;

    public string SnapshotsDirectory { get; }

    public static SchemaSnapshotCatalogService Instance { get; } = new();

    public SchemaSnapshotCatalogService(
        ISchemaSnapshotStorageService? storageService = null,
        string? snapshotsDirectory = null)
    {
        _storageService = storageService ?? SchemaSnapshotStorageService.Instance;
        SnapshotsDirectory = snapshotsDirectory ?? AppConstants.DefaultSnapshotsDirectory;
        Directory.CreateDirectory(SnapshotsDirectory);
    }

    public async Task<IReadOnlyList<SnapshotCatalogItem>> GetAllAsync(
        string? searchQuery = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(SnapshotsDirectory))
        {
            return Array.Empty<SnapshotCatalogItem>();
        }

        var files = Directory.EnumerateFiles(SnapshotsDirectory, "*.*")
            .Where(f => f.EndsWith(".rschema", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var items = new List<SnapshotCatalogItem>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await ReadCatalogItemSafeAsync(file, cancellationToken);
            items.Add(item);
        }

        IEnumerable<SnapshotCatalogItem> query = items;

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var q = searchQuery.Trim();
            query = query.Where(i =>
                (!string.IsNullOrEmpty(i.Label) && i.Label.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(i.DatabaseName) && i.DatabaseName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(i.EngineType) && i.EngineType.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(i.FileName) && i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(i.Notes) && i.Notes.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(i => i.CreatedAt).ToList();
    }

    public async Task<SnapshotCatalogItem?> GetByFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await ReadCatalogItemSafeAsync(filePath, cancellationToken);
    }

    public async Task<string> SaveToCatalogAsync(
        SchemaSnapshotModel snapshot,
        string? label = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Directory.CreateDirectory(SnapshotsDirectory);

        string dbName = string.IsNullOrWhiteSpace(snapshot.DatabaseName) ? "database" : snapshot.DatabaseName;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            dbName = dbName.Replace(invalidChar, '_');
        }

        string fileName = $"{dbName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.rschema";
        string targetPath = Path.Combine(SnapshotsDirectory, fileName);

        int counter = 1;
        while (File.Exists(targetPath))
        {
            fileName = $"{dbName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{counter++}.rschema";
            targetPath = Path.Combine(SnapshotsDirectory, fileName);
        }

        if (!string.IsNullOrEmpty(label))
        {
            snapshot.Label = label;
        }
        if (!string.IsNullOrEmpty(notes))
        {
            snapshot.Notes = notes;
        }

        await _storageService.SaveSnapshotAsync(targetPath, snapshot, compressGzip: true, cancellationToken: cancellationToken);
        return targetPath;
    }

    public Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    public async Task RenameAsync(
        string filePath,
        string newLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newLabel);

        var envelope = await _storageService.LoadSnapshotAsync(filePath, allowCompromised: true, cancellationToken);
        envelope.Metadata.Label = newLabel;
        envelope.Snapshot.Label = newLabel;

        bool isGzip = filePath.EndsWith(".rschema", StringComparison.OrdinalIgnoreCase);
        await _storageService.SaveSnapshotAsync(filePath, envelope.Snapshot, envelope.Metadata, compressGzip: isGzip, cancellationToken: cancellationToken);
    }

    public async Task ExportToFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source snapshot file not found: {sourceFilePath}", sourceFilePath);
        }

        var destDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        string tempPath = destinationFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            const int bufferSize = 65536;
            var sourceOptions = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = bufferSize,
                Options = FileOptions.Asynchronous
            };

            var destOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = bufferSize,
                Options = FileOptions.Asynchronous
            };

            await using (var source = new FileStream(sourceFilePath, sourceOptions))
            await using (var dest = new FileStream(tempPath, destOptions))
            {
                await source.CopyToAsync(dest, bufferSize, cancellationToken);
                await dest.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, destinationFilePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    private async Task<SnapshotCatalogItem> ReadCatalogItemSafeAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var item = new SnapshotCatalogItem
        {
            FilePath = filePath,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            CreatedAt = fileInfo.Exists ? fileInfo.CreationTimeUtc : DateTime.UtcNow,
            IsIntegrityValid = true
        };

        if (!fileInfo.Exists)
        {
            item.IsIntegrityValid = false;
            item.IntegrityError = "Arquivo não encontrado.";
            return item;
        }

        try
        {
            // Inspect magic bytes
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
            {
                byte[] magic = new byte[2];
                int read = await fs.ReadAsync(magic.AsMemory(0, 2), cancellationToken);
                if (read == 2 && magic[0] == SchemaSnapshotStorageService.GZipMagic1 && magic[1] == SchemaSnapshotStorageService.GZipMagic2)
                {
                    item.IsGzipCompressed = true;
                }
            }

            var envelope = await _storageService.LoadSnapshotAsync(filePath, allowCompromised: true, cancellationToken);
            item.Label = !string.IsNullOrEmpty(envelope.Metadata.Label)
                ? envelope.Metadata.Label
                : (!string.IsNullOrEmpty(envelope.Snapshot.Label) ? envelope.Snapshot.Label : Path.GetFileNameWithoutExtension(filePath));

            item.DatabaseName = !string.IsNullOrEmpty(envelope.Metadata.DatabaseName)
                ? envelope.Metadata.DatabaseName
                : (!string.IsNullOrEmpty(envelope.Snapshot.DatabaseName) ? envelope.Snapshot.DatabaseName : "Desconhecido");

            item.EngineType = !string.IsNullOrEmpty(envelope.Metadata.EngineType)
                ? envelope.Metadata.EngineType
                : (!string.IsNullOrEmpty(envelope.Snapshot.EngineType) ? envelope.Snapshot.EngineType : "PostgreSQL");

            item.CreatedAt = envelope.ExportedAt != default
                ? envelope.ExportedAt
                : (envelope.Snapshot.CreatedAt != default ? envelope.Snapshot.CreatedAt : fileInfo.CreationTimeUtc);

            item.TableCount = envelope.Metadata.TableCount > 0 ? envelope.Metadata.TableCount : (envelope.Snapshot.Tables?.Count ?? 0);
            item.ViewCount = envelope.Metadata.ViewCount > 0 ? envelope.Metadata.ViewCount : (envelope.Snapshot.Views?.Count ?? 0);
            item.TriggerCount = envelope.Metadata.TriggerCount > 0 ? envelope.Metadata.TriggerCount : (envelope.Snapshot.Triggers?.Count ?? 0);
            item.Notes = envelope.Metadata.Notes ?? envelope.Snapshot.Notes;
            item.IsIntegrityValid = !envelope.Snapshot.IsIntegrityCompromised;

            if (envelope.Snapshot.IsIntegrityCompromised)
            {
                item.IntegrityError = "Checksum SHA-256 divergente do conteúdo do snapshot.";
            }
        }
        catch (Exception ex)
        {
            item.IsIntegrityValid = false;
            item.IntegrityError = ex.Message;
            item.Label = Path.GetFileNameWithoutExtension(filePath);
            item.DatabaseName = "Arquivo Corrompido";
            item.EngineType = "Desconhecido";
        }

        return item;
    }
}
