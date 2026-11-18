using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models.Diff;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de gerenciamento do catálogo local de snapshots (~/.runix/snapshots/).
/// </summary>
public interface ISchemaSnapshotCatalogService
{
    string SnapshotsDirectory { get; }

    Task<IReadOnlyList<SnapshotCatalogItem>> GetAllAsync(
        string? searchQuery = null,
        CancellationToken cancellationToken = default);

    Task<SnapshotCatalogItem?> GetByFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<string> SaveToCatalogAsync(
        SchemaSnapshotModel snapshot,
        string? label = null,
        string? notes = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task RenameAsync(
        string filePath,
        string newLabel,
        CancellationToken cancellationToken = default);

    Task ExportToFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken = default);
}
