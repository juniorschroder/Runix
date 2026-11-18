using System;
using System.Text.Json.Serialization;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Envelope canônico do arquivo offline .rschema com metadados e validação SHA-256.
/// </summary>
public class SchemaSnapshotEnvelope
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.4.0";

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("checksumSha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public SnapshotMetadata Metadata { get; set; } = new();

    [JsonPropertyName("snapshot")]
    public SchemaSnapshotModel Snapshot { get; set; } = new();
}
