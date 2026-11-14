using System.Collections.Generic;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato para geração de scripts DDL específicos por dialeto a partir de modelos de DER.
/// </summary>
public interface IDdlGenerator
{
    string GenerateCreateTableSql(ErdTableNode node, DatabaseType type);
    string GenerateFullSchemaSql(ErdGraphModel graph, DatabaseType type);
}
