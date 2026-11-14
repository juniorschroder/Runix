using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Plano compilado de migração contendo a lista ordenada de operações e o script DDL final gerado.
/// </summary>
public class MigrationPlan
{
    public DatabaseType DatabaseType { get; set; }
    public List<MigrationOperation> Operations { get; set; } = new();
    public string GeneratedSql { get; set; } = string.Empty;
    public bool RequiresTableRebuild { get; set; }
    public int StepsCount => Operations.Count;

    public bool HasOperations => Operations.Count > 0 || !string.IsNullOrWhiteSpace(GeneratedSql);
}
