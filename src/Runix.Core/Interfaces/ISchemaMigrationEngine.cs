using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Motor de orquestração de migrações DDL para planejamento, compilação de scripts dialetais e execução atômica resiliente.
/// </summary>
public interface ISchemaMigrationEngine
{
    MigrationPlan CreatePlan(TableSchemaDiff diff, DatabaseType dbType);
    MigrationPlan CreateForeignKeyPlan(ForeignKeyDefinition fk, DatabaseType dbType);
    Task<QueryResult> ExecutePlanAsync(IDatabaseService databaseService, MigrationPlan plan, CancellationToken ct = default);
}
