using System.Text.Json.Serialization;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Metadados descritivos e sumários gravados no cabeçalho do envelope de um snapshot .rschema.
/// </summary>
public class SnapshotMetadata
{
    [JsonPropertyName("databaseName")]
    public string DatabaseName { get; set; } = string.Empty;

    [JsonPropertyName("engineType")]
    public string EngineType { get; set; } = string.Empty;

    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; set; }

    [JsonPropertyName("tableCount")]
    public int TableCount { get; set; }

    [JsonPropertyName("viewCount")]
    public int ViewCount { get; set; }

    [JsonPropertyName("triggerCount")]
    public int TriggerCount { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("environment")]
    public string? Environment { get; set; }
}
