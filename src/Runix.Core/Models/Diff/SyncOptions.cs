using System.Collections.Generic;

namespace Runix.Core.Models.Diff;

public enum SyncDirection
{
    SourceToTarget,
    TargetToSource
}

public class SyncOptions
{
    public DatabaseType TargetDialect { get; set; } = DatabaseType.PostgreSQL;
    public bool SafeMode { get; set; } = true;
    public SyncDirection Direction { get; set; } = SyncDirection.SourceToTarget;
    public HashSet<string>? SelectedTableNames { get; set; }

    public bool IsTableSelected(string tableName)
    {
        if (SelectedTableNames == null) return true;
        return SelectedTableNames.Contains(tableName);
    }
}
