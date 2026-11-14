using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato para geradores de instruções DDL específicas por dialeto de banco de dados.
/// </summary>
public interface ISqlDialectMigrationGenerator
{
    DatabaseType SupportedType { get; }
    string GenerateSql(MigrationPlan plan);
    string GenerateCreateTable(ErdTableNode node);
    string GenerateAddForeignKey(ForeignKeyDefinition fk);
}
