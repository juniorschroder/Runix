namespace Runix.Core.Models.Diff;

/// <summary>
/// Opções granulares para controlar quais elementos de schema são extraídos para o snapshot.
/// </summary>
public class SnapshotExtractionOptions
{
    public bool IncludeTables { get; set; } = true;
    public bool IncludeForeignKeys { get; set; } = true;
    public bool IncludeIndexes { get; set; } = true;
    public bool IncludeViews { get; set; } = true;
    public bool IncludeTriggers { get; set; } = true;
    public bool IncludeCheckConstraints { get; set; } = true;
}
