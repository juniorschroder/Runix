using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Runix.Core.Models;

public enum HealthSeverity
{
    Healthy,
    Warning,
    Critical
}

public enum MaintenanceOperationType
{
    Vacuum,
    Analyze,
    VacuumAnalyze,
    Reindex,
    IntegrityCheck
}

public sealed class TableStorageMetric
{
    public string Schema { get; init; } = "main";
    public string TableName { get; init; } = string.Empty;
    public long RowCount { get; init; }
    public long DataSizeBytes { get; init; }
    public long IndexSizeBytes { get; init; }
    public long TotalSizeBytes => DataSizeBytes + IndexSizeBytes;
    public long DeadTuples { get; init; }
    public long SeqScans { get; init; }
    public long IdxScans { get; init; }
    public int IndexCount { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Schema) || string.Equals(Schema, "main", StringComparison.OrdinalIgnoreCase)
        ? TableName
        : $"{Schema}.{TableName}";

    public string DataSizeFormatted => DatabaseHealthReport.FormatBytes(DataSizeBytes);
    public string IndexSizeFormatted => DatabaseHealthReport.FormatBytes(IndexSizeBytes);
    public string TotalSizeFormatted => DatabaseHealthReport.FormatBytes(TotalSizeBytes);

    public double DeadTupleRatioPercent => (RowCount + DeadTuples) > 0
        ? Math.Round((double)DeadTuples * 100.0 / (RowCount + DeadTuples), 1)
        : 0.0;
}

public sealed class OracleTablespaceMetric
{
    public string TablespaceName { get; init; } = string.Empty;
    public long TotalSizeBytes { get; init; }
    public long UsedSizeBytes { get; init; }
    public long FreeSizeBytes { get; init; }
    public int FileCount { get; init; }
    public int AutoextensibleCount { get; init; }
    public double UsedPercent => TotalSizeBytes > 0 ? Math.Round((double)UsedSizeBytes * 100.0 / TotalSizeBytes, 1) : 0.0;
    public string TotalSizeFormatted => DatabaseHealthReport.FormatBytes(TotalSizeBytes);
    public string UsedSizeFormatted => DatabaseHealthReport.FormatBytes(UsedSizeBytes);
    public string FreeSizeFormatted => DatabaseHealthReport.FormatBytes(FreeSizeBytes);
}

public sealed class IndexHealthFinding
{
    public string Schema { get; init; } = "main";
    public string TableName { get; init; } = string.Empty;
    public string IndexName { get; init; } = string.Empty;
    public string FindingType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ColumnsSummary { get; init; } = string.Empty;
    public string RecommendationSql { get; init; } = string.Empty;
    public HealthSeverity Severity { get; init; } = HealthSeverity.Warning;

    public string SeverityBadge => Severity switch
    {
        HealthSeverity.Critical => "CRÍTICO",
        HealthSeverity.Warning => "ATENÇÃO",
        _ => "INFO"
    };

    public string SeverityColor => Severity switch
    {
        HealthSeverity.Critical => "#f87171",
        HealthSeverity.Warning => "#fbbf24",
        _ => "#38bdf8"
    };
}

public sealed class IntegrityCheckIssue
{
    public string CheckType { get; init; } = "IntegrityCheck";
    public string TableName { get; init; } = string.Empty;
    public string RowId { get; init; } = string.Empty;
    public string ParentTable { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class MaintenanceExecutionResult
{
    public bool Success { get; init; }
    public MaintenanceOperationType Operation { get; init; }
    public string? TargetTable { get; init; }
    public string ExecutedSql { get; init; } = string.Empty;
    public long SizeBeforeBytes { get; init; }
    public long SizeAfterBytes { get; init; }
    public long ReclaimedBytes => Math.Max(0, SizeBeforeBytes - SizeAfterBytes);
    public string ReclaimedFormatted => DatabaseHealthReport.FormatBytes(ReclaimedBytes);
    public double ElapsedMilliseconds { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<IntegrityCheckIssue> IntegrityIssues { get; init; } = new();
}

public sealed class DatabaseHealthReport
{
    public DatabaseType DatabaseType { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public DateTime CollectedAtUtc { get; init; } = DateTime.UtcNow;
    public long TotalSizeBytes { get; init; }
    public long DataSizeBytes { get; init; }
    public long IndexSizeBytes { get; init; }
    public long FreeOrReclaimableBytes { get; init; }
    public double CacheOrPageEfficiencyPercent { get; init; } = 100.0;
    public double FragmentationOrDeadTuplePercent { get; init; }
    public int TableCount { get; init; }
    public int ViewCount { get; init; }
    public int IndexCount { get; init; }
    public int ActiveConnections { get; init; } = 1;
    public int MaxConnections { get; init; } = 100;
    public int BlockedSessions { get; init; }
    public string JournalOrWalMode { get; init; } = "WAL";
    public int PageSize { get; init; } = 4096;
    public long PageCount { get; init; }
    public long FreelistCount { get; init; }
    public HealthSeverity OverallSeverity { get; init; } = HealthSeverity.Healthy;
    public string StatusSummary { get; init; } = "Banco de dados operando com saúde ideal.";
    public List<TableStorageMetric> TableMetrics { get; init; } = new();
    public List<OracleTablespaceMetric> TablespaceMetrics { get; init; } = new();
    public List<IndexHealthFinding> IndexFindings { get; init; } = new();
    public List<IntegrityCheckIssue> IntegrityIssues { get; init; } = new();

    public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);
    public string DataSizeFormatted => FormatBytes(DataSizeBytes);
    public string IndexSizeFormatted => FormatBytes(IndexSizeBytes);
    public string FreeSizeFormatted => FormatBytes(FreeOrReclaimableBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F2} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }

    public string ToMarkdownReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Relatório de Saúde do Banco — {TargetName} ({DatabaseType})");
        sb.AppendLine($"- **Coletado em (UTC):** {CollectedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **Status Geral:** {OverallSeverity} — {StatusSummary}");
        sb.AppendLine($"- **Tamanho Total:** {TotalSizeFormatted} (Dados: {DataSizeFormatted} | Índices: {IndexSizeFormatted} | Recuperável: {FreeSizeFormatted})");
        sb.AppendLine($"- **Eficiência de Cache / Páginas:** {CacheOrPageEfficiencyPercent:F1}%");
        sb.AppendLine($"- **Fragmentação / Dead Tuples:** {FragmentationOrDeadTuplePercent:F1}%");
        sb.AppendLine($"- **Objetos:** {TableCount} tabelas, {ViewCount} views, {IndexCount} índices");
        if (BlockedSessions > 0)
        {
            sb.AppendLine($"- **Sessões Bloqueadas (Locks):** ⚠ {BlockedSessions} sessão(ões) em espera");
        }
        sb.AppendLine();

        if (TablespaceMetrics.Count > 0)
        {
            sb.AppendLine("## Tablespaces Oracle");
            sb.AppendLine("| Tablespace | Total | Usado | Livre | % Utilização | Arquivos | Autoextensíveis |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var ts in TablespaceMetrics)
            {
                sb.AppendLine($"| {ts.TablespaceName} | {ts.TotalSizeFormatted} | {ts.UsedSizeFormatted} | {ts.FreeSizeFormatted} | {ts.UsedPercent:F1}% | {ts.FileCount} | {ts.AutoextensibleCount} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Armazenamento por Tabela");
        sb.AppendLine("| Tabela | Linhas | Dados | Índices | Total | Dead Tuples |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach (var t in TableMetrics)
        {
            sb.AppendLine($"| {t.DisplayName} | {t.RowCount:N0} | {t.DataSizeFormatted} | {t.IndexSizeFormatted} | {t.TotalSizeFormatted} | {t.DeadTuples:N0} |");
        }

        if (IndexFindings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Diagnóstico de Índices");
            foreach (var f in IndexFindings)
            {
                sb.AppendLine($"- **[{f.SeverityBadge}] {f.TableName}.{f.IndexName}** ({f.FindingType}): {f.Description}");
            }
        }

        if (IntegrityIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Inconsistências de Integridade");
            foreach (var iss in IntegrityIssues)
            {
                sb.AppendLine($"- **[{iss.CheckType}] {iss.TableName}:** {iss.Description}");
            }
        }

        return sb.ToString();
    }

    public string ToJsonReport()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
