using System.Collections.Generic;

namespace Runix.Core.Models;

public enum BackupFormat
{
    SqlScript,
    CompressedSqlGzip,
    SqliteBinary
}

public enum BackupScope
{
    Full,
    SchemaOnly,
    DataOnly
}

public class BackupOptions
{
    public string OutputFilePath { get; set; } = string.Empty;
    public BackupFormat Format { get; set; } = BackupFormat.SqlScript;
    public BackupScope Scope { get; set; } = BackupScope.Full;
    public bool IncludeDropStatements { get; set; } = true;
    public bool IncludeProgrammableObjects { get; set; } = true;
    public List<string>? SelectedTables { get; set; }
}

public class RestoreOptions
{
    public string InputFilePath { get; set; } = string.Empty;
}

public class BackupProgress
{
    public string CurrentPhase { get; set; } = string.Empty;
    public int ProcessedTables { get; set; }
    public int TotalTables { get; set; }
    public long ProcessedRows { get; set; }
    public double Percentage => TotalTables > 0 ? (ProcessedTables * 100.0) / TotalTables : 0;
}

public class BackupResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int TablesProcessed { get; set; }
    public long RowsProcessed { get; set; }
    public int StatementsExecuted { get; set; }
    public long ElapsedMilliseconds { get; set; }
}
