using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Tipos de operações atômicas suportadas pelo motor de migração DDL.
/// </summary>
public enum MigrationOperationType
{
    CreateTable,
    DropTable,
    RenameTable,
    AddColumn,
    DropColumn,
    AlterColumn,
    AddForeignKey,
    DropForeignKey,
    RebuildTableSqlite
}

/// <summary>
/// Representa uma operação elementar ou composta de modificação de schema.
/// </summary>
public class MigrationOperation
{
    public MigrationOperationType Type { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string? NewTableName { get; set; }
    public ColumnInfo? Column { get; set; }
    public ColumnInfo? OldColumn { get; set; }
    public ForeignKeyDefinition? ForeignKey { get; set; }
    public List<ColumnInfo> ExistingColumns { get; set; } = new();
    public List<ColumnInfo> TargetColumns { get; set; } = new();
    public List<ForeignKeyDefinition> TargetForeignKeys { get; set; } = new();
}
