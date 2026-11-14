using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Migrations;

/// <summary>
/// Motor central de migração de schemas relacionais para planejamento e execução atômica resiliente.
/// </summary>
public class SchemaMigrationEngine : ISchemaMigrationEngine
{
    private readonly Dictionary<DatabaseType, ISqlDialectMigrationGenerator> _generators = new();

    public SchemaMigrationEngine()
    {
        RegisterGenerator(new SqliteMigrationGenerator());
        RegisterGenerator(new PostgresMigrationGenerator());
    }

    public void RegisterGenerator(ISqlDialectMigrationGenerator generator)
    {
        _generators[generator.SupportedType] = generator;
    }

    public MigrationPlan CreatePlan(TableSchemaDiff diff, DatabaseType dbType)
    {
        if (!_generators.TryGetValue(dbType, out var generator))
        {
            throw new NotSupportedException($"Dialeto de banco de dados não suportado para migrações: {dbType}");
        }

        var plan = new MigrationPlan { DatabaseType = dbType };

        // 1. Criação de nova tabela
        if (diff.IsNewTable)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.CreateTable,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                TargetColumns = diff.TargetColumns.ToList(),
                TargetForeignKeys = diff.AddedForeignKeys.ToList()
            });

            plan.GeneratedSql = generator.GenerateSql(plan);
            return plan;
        }

        // 2. Modificação de tabela existente no SQLite
        if (dbType == DatabaseType.SQLite)
        {
            var needsRebuild = diff.RemovedColumns.Count > 0 ||
                               diff.AlteredColumns.Count > 0 ||
                               diff.AddedForeignKeys.Count > 0 ||
                               diff.RemovedForeignKeys.Count > 0 ||
                               diff.AddedColumns.Any(c => !c.IsNullable && string.IsNullOrWhiteSpace(c.DefaultValue) && !c.IsPrimaryKey);

            if (needsRebuild)
            {
                plan.RequiresTableRebuild = true;
                plan.Operations.Add(new MigrationOperation
                {
                    Type = MigrationOperationType.RebuildTableSqlite,
                    TableName = diff.OriginalTableName,
                    NewTableName = diff.TargetTableName,
                    Schema = diff.Schema,
                    ExistingColumns = diff.OriginalColumns.ToList(),
                    TargetColumns = diff.TargetColumns.ToList(),
                    TargetForeignKeys = diff.AddedForeignKeys.ToList()
                });
            }
            else
            {
                if (diff.IsRenamed)
                {
                    plan.Operations.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.RenameTable,
                        TableName = diff.OriginalTableName,
                        NewTableName = diff.TargetTableName
                    });
                }

                foreach (var col in diff.AddedColumns)
                {
                    plan.Operations.Add(new MigrationOperation
                    {
                        Type = MigrationOperationType.AddColumn,
                        TableName = diff.TargetTableName,
                        Column = col
                    });
                }
            }

            plan.GeneratedSql = generator.GenerateSql(plan);
            return plan;
        }

        // 3. Modificação de tabela existente no PostgreSQL
        if (diff.IsRenamed)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.RenameTable,
                TableName = diff.OriginalTableName,
                NewTableName = diff.TargetTableName,
                Schema = diff.Schema
            });
        }

        foreach (var col in diff.RemovedColumns)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.DropColumn,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                Column = col
            });
        }

        foreach (var col in diff.AddedColumns)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.AddColumn,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                Column = col
            });
        }

        foreach (var (oldCol, newCol) in diff.AlteredColumns)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.AlterColumn,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                OldColumn = oldCol,
                Column = newCol
            });
        }

        foreach (var fk in diff.RemovedForeignKeys)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.DropForeignKey,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                ForeignKey = fk
            });
        }

        foreach (var fk in diff.AddedForeignKeys)
        {
            plan.Operations.Add(new MigrationOperation
            {
                Type = MigrationOperationType.AddForeignKey,
                TableName = diff.TargetTableName,
                Schema = diff.Schema,
                ForeignKey = fk
            });
        }

        plan.GeneratedSql = generator.GenerateSql(plan);
        return plan;
    }

    public MigrationPlan CreateForeignKeyPlan(ForeignKeyDefinition fk, DatabaseType dbType)
    {
        if (!_generators.TryGetValue(dbType, out var generator))
        {
            throw new NotSupportedException($"Dialeto de banco de dados não suportado para migrações: {dbType}");
        }

        var plan = new MigrationPlan
        {
            DatabaseType = dbType,
            RequiresTableRebuild = dbType == DatabaseType.SQLite
        };

        plan.Operations.Add(new MigrationOperation
        {
            Type = MigrationOperationType.AddForeignKey,
            TableName = fk.SourceTable,
            ForeignKey = fk
        });

        plan.GeneratedSql = generator.GenerateAddForeignKey(fk);
        return plan;
    }

    public async Task<QueryResult> ExecutePlanAsync(IDatabaseService databaseService, MigrationPlan plan, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plan.GeneratedSql))
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = "Nenhum script DDL foi gerado para execução."
            };
        }

        try
        {
            // O serviço de banco executa o script sob o pipeline resiliente Vard
            return await databaseService.ExecuteQueryAsync(plan.GeneratedSql);
        }
        catch (Exception ex)
        {
            return new QueryResult
            {
                Success = false,
                ErrorMessage = $"Falha na execução da migração: {ex.Message}"
            };
        }
    }
}
