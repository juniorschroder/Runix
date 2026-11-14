using System;

namespace Runix.Core.Models.Dba;

/// <summary>
/// Representa uma sessão / processo ativo no servidor PostgreSQL (pg_stat_activity).
/// </summary>
public class PostgresActiveSession
{
    public int Pid { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string ClientAddress { get; set; } = string.Empty;
    public int? ClientPort { get; set; }
    public DateTime? BackendStart { get; set; }
    public DateTime? QueryStart { get; set; }
    public DateTime? StateChange { get; set; }
    public string State { get; set; } = "idle";
    public string QueryText { get; set; } = string.Empty;
    public string BackendType { get; set; } = "client backend";
    public bool IsCurrentSession { get; set; }
    public double ElapsedSeconds { get; set; }

    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);

    public bool IsSlowQuery => IsActive && ElapsedSeconds >= 5.0;

    public bool IsCriticalSlowQuery => IsActive && ElapsedSeconds >= 30.0;

    public string DurationFormatted
    {
        get
        {
            if (ElapsedSeconds <= 0) return "0s";
            var ts = TimeSpan.FromSeconds(ElapsedSeconds);
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            }
            if (ts.TotalMinutes >= 1)
            {
                return $"{ts.Minutes}m {ts.Seconds:D2}s";
            }
            return $"{ElapsedSeconds:F1}s";
        }
    }

    public string StateColor
    {
        get
        {
            return State.ToLowerInvariant() switch
            {
                "active" => IsCriticalSlowQuery ? "#f87171" : (IsSlowQuery ? "#fbbf24" : "#34d399"),
                "idle" => "#60a5fa",
                "idle in transaction" => "#fbbf24",
                "idle in transaction (aborted)" => "#f87171",
                "fastpath function call" => "#a78bfa",
                "disabled" => "#64748b",
                _ => "#94a3b8"
            };
        }
    }
}
