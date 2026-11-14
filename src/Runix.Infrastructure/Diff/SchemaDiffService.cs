using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Core.Models.Diff;

namespace Runix.Infrastructure.Diff;

/// <summary>
/// Serviço unificado de extração de snapshots estruturais, comparação diferencial e execução de DDL.
/// </summary>
public class SchemaDiffService : ISchemaDiffService
{
    public static SchemaDiffService Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<SchemaSnapshotModel> ExtractSnapshotAsync(IDatabaseService db)
    {
        var snapshot = new SchemaSnapshotModel
        {
            Version = 1,
            DatabaseName = db.Config.Type == DatabaseType.SQLite 
                ? System.IO.Path.GetFileName(db.Config.FilePath ?? "database.db") 
                : (db.Config.Database ?? "postgres"),
            EngineType = db.Config.Type.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        var tables = await db.GetTablesAsync();

        foreach (var tbl in tables)
        {
            var tableSnap = new TableSnapshot
            {
                Name = tbl.Name,
                Schema = tbl.Schema
            };

            // Columns
            try
            {
                var cols = await db.GetTableColumnsAsync(tbl.Name);
                foreach (var c in cols)
                {
                    tableSnap.Columns.Add(new ColumnSnapshot
                    {
                        Name = c.Name,
                        DataType = c.DataType,
                        IsNullable = c.IsNullable,
                        IsPrimaryKey = c.IsPrimaryKey,
                        DefaultValue = c.DefaultValue,
                        OrdinalPosition = c.OrdinalPosition
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching columns for {tbl.Name}: {ex.Message}");
            }

            // Indexes
            try
            {
                var indexes = await db.GetTableIndexesAsync(tbl.Name);
                foreach (var idx in indexes)
                {
                    tableSnap.Indexes.Add(new IndexSnapshot
                    {
                        Name = idx.Name,
                        IsUnique = idx.IsUnique,
                        IsPrimaryKey = idx.IsPrimaryKey,
                        Columns = idx.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching indexes for {tbl.Name}: {ex.Message}");
            }

            // Foreign Keys
            try
            {
                var fks = await db.GetForeignKeysAsync(tbl.Name);
                foreach (var fk in fks)
                {
                    tableSnap.ForeignKeys.Add(new ForeignKeySnapshot
                    {
                        ConstraintName = fk.ConstraintName,
                        ColumnName = fk.SourceColumn,
                        ForeignTableName = fk.TargetTable,
                        ForeignColumnName = fk.TargetColumn,
                        OnUpdate = fk.OnUpdate,
                        OnDelete = fk.OnDelete
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchemaDiff] Error fetching foreign keys for {tbl.Name}: {ex.Message}");
            }

            snapshot.Tables.Add(tableSnap);
        }

        return snapshot;
    }

    public string SerializeSnapshot(SchemaSnapshotModel snapshot)
    {
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public SchemaSnapshotModel DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SchemaSnapshotModel();
        }

        return JsonSerializer.Deserialize<SchemaSnapshotModel>(json, JsonOptions) ?? new SchemaSnapshotModel();
    }

    public SchemaDiffResult CompareSnapshots(SchemaSnapshotModel source, SchemaSnapshotModel target, bool invertDirection = false)
    {
        return SchemaDiffEngine.Compare(source, target, invertDirection);
    }

    public string GenerateSyncDdl(SchemaDiffResult diffResult, SyncOptions options)
    {
        return SyncDdlGenerator.Generate(diffResult, options);
    }

    public async Task<QueryResult> ApplySyncDdlAsync(IDatabaseService targetDb, string ddlScript)
    {
        if (string.IsNullOrWhiteSpace(ddlScript))
        {
            return new QueryResult { Success = true, AffectedRows = 0 };
        }

        // For SQLite, multiple DDL statements in a single command are supported by Microsoft.Data.Sqlite
        return await targetDb.ExecuteQueryAsync(ddlScript);
    }
}
