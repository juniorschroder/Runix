namespace Runix.Core.Models;

public class TableIndexInfo
{
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string Method { get; set; } = "BTREE";
    public string? Definition { get; set; }

    public string UniqueText => IsUnique ? "YES" : "NO";
    public string PrimaryText => IsPrimaryKey ? "🔑 PK" : "";

    public override string ToString() => $"{Name} ({Columns})";
}
