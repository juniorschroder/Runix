using System;

namespace Runix.Core.Models;

/// <summary>
/// Registro histórico detalhado de uma consulta SQL executada pelo usuário.
/// </summary>
public class QueryHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? ConnectionId { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public long ElapsedMilliseconds { get; set; }
    public long? RowCount { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public string ElapsedFormatted => $"{ElapsedMilliseconds} ms";
    public string RowsFormatted => RowCount.HasValue ? $"{RowCount.Value:N0} lin" : "-";
    public string StatusIcon => Success ? "✓" : "✕";
    public string Summary => $"{StatusIcon} {ElapsedFormatted} • {RowsFormatted}";
}
