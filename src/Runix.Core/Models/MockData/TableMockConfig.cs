using System.Collections.Generic;
using Runix.Core.Models;

namespace Runix.Core.Models.MockData;

/// <summary>
/// Configuração global para geração de mock data em uma tabela.
/// </summary>
public class TableMockConfig
{
    public string TableName { get; set; } = string.Empty;
    public int RowCount { get; set; } = 50;
    public DatabaseType Dialect { get; set; } = DatabaseType.SQLite;
    public List<ColumnGeneratorRule> Rules { get; set; } = new();
}
