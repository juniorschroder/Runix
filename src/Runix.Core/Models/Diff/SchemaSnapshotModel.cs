using System;
using System.Collections.Generic;

namespace Runix.Core.Models.Diff;

/// <summary>
/// Representa a fotografia estrutural completa de um banco de dados, serializável em JSON.
/// </summary>
public class SchemaSnapshotModel
{
    public int Version { get; set; } = 1;
    public string DatabaseName { get; set; } = string.Empty;
    public string EngineType { get; set; } = "PostgreSQL";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TableSnapshot> Tables { get; set; } = new();
}

public class TableSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string? Comment { get; set; }
    public List<ColumnSnapshot> Columns { get; set; } = new();
    public List<IndexSnapshot> Indexes { get; set; } = new();
    public List<ForeignKeySnapshot> ForeignKeys { get; set; } = new();

    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

public class ColumnSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "text";
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    public string? DefaultValue { get; set; }
    public int OrdinalPosition { get; set; }
}

public class IndexSnapshot
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public List<string> Columns { get; set; } = new();
}

public class ForeignKeySnapshot
{
    public string ConstraintName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ForeignTableName { get; set; } = string.Empty;
    public string ForeignColumnName { get; set; } = string.Empty;
    public string OnUpdate { get; set; } = "NO ACTION";
    public string OnDelete { get; set; } = "NO ACTION";
}
