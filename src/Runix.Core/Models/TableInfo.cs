using System;

namespace Runix.Core.Models;

public class TableInfo
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string Type { get; set; } = "BASE TABLE"; // or VIEW
    public long? RowCount { get; set; }
    public bool IsView => Type.Equals("VIEW", StringComparison.OrdinalIgnoreCase);

    public string Icon => IsView ? "👁" : "🗄";
    public string DisplayName => string.IsNullOrEmpty(Schema) || Schema == "public" || Schema == "main"
        ? Name
        : $"{Schema}.{Name}";

    public override string ToString() => Name;
}

public class ColumnInfo
{
    public string TableName { get; set; } = string.Empty;
    public int OrdinalPosition { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsForeignKey { get; set; }
    public string? ForeignKeyReference { get; set; }
    public string? DefaultValue { get; set; }

    public string NullableText => IsNullable ? "YES" : "NO";
    public string KeyText
    {
        get
        {
            if (IsPrimaryKey && IsForeignKey) return "🔑 PK, 🔗 FK";
            if (IsPrimaryKey) return "🔑 PK";
            if (IsForeignKey) return string.IsNullOrEmpty(ForeignKeyReference) ? "🔗 FK" : $"🔗 FK → {ForeignKeyReference}";
            return "";
        }
    }
}
