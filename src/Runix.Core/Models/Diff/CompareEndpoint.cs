using System;
using Runix.Core.Models;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Tipos de endpoint suportados para comparação de schema (banco ao vivo ou snapshot offline).
/// </summary>
public enum CompareEndpointType
{
    ActiveConnection,
    SavedConnection,
    LocalCatalogSnapshot,
    SnapshotFile
}

/// <summary>
/// Abstração unificada de uma origem ou destino de comparação de schema.
/// </summary>
public class CompareEndpoint
{
    public CompareEndpointType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Identifier { get; set; }
    public ConnectionConfig? SavedConnection { get; set; }
    public SchemaSnapshotModel? Snapshot { get; set; }
    public string? FilePath { get; set; }
    public SnapshotCatalogItem? CatalogItem { get; set; }
    public DatabaseType Dialect { get; set; } = DatabaseType.PostgreSQL;

    public bool IsLiveDatabase => Type is CompareEndpointType.ActiveConnection or CompareEndpointType.SavedConnection;
    public bool IsOfflineSnapshot => Type is CompareEndpointType.LocalCatalogSnapshot or CompareEndpointType.SnapshotFile;

    public override string ToString() => DisplayName;
}
