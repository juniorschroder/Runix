using System;
using System.IO;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Modelo de exibição para snapshots listados no catálogo local (~/.runix/snapshots/).
/// </summary>
public class SnapshotCatalogItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Label { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string EngineType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TableCount { get; set; }
    public int ViewCount { get; set; }
    public int TriggerCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string FormattedFileSize => FormatFileSize(FileSizeBytes);
    public bool IsGzipCompressed { get; set; }
    public bool IsIntegrityValid { get; set; } = true;
    public string? IntegrityError { get; set; }
    public string? Notes { get; set; }

    public static string FormatFileSize(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{(bytes / 1024.0):F1} KB",
            _ => $"{(bytes / (1024.0 * 1024.0)):F2} MB"
        };
}
