using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Models.MockData;

namespace Runix.Core.Interfaces;

/// <summary>
/// Serviço de geração de dados fictícios semânticos com amostragem de FK e seeding resiliente.
/// </summary>
public interface IMockDataGeneratorService
{
    /// <summary>
    /// Infere regras de geração semântica a partir dos nomes e tipos das colunas e chaves estrangeiras.
    /// </summary>
    List<ColumnGeneratorRule> InferRules(TableInfo table, List<ColumnInfo> columns, List<ForeignKeyInfo> foreignKeys);

    /// <summary>
    /// Amostra chaves primárias existentes das tabelas-pai para as colunas configuradas como ForeignKeySample.
    /// </summary>
    Task SampleForeignKeysAsync(IDatabaseService db, List<ColumnGeneratorRule> rules, CancellationToken ct = default);

    /// <summary>
    /// Gera linhas em memória estruturadas como dicionários (coluna -> valor).
    /// </summary>
    List<Dictionary<string, object?>> GenerateRows(TableMockConfig config, int count);

    /// <summary>
    /// Gera um script SQL completo com comandos INSERT multi-linha respeitando o dialeto especificado.
    /// </summary>
    string GenerateSqlScript(TableMockConfig config, List<Dictionary<string, object?>> rows, DatabaseType dialect);

    /// <summary>
    /// Insere as linhas geradas no banco de dados de forma transacional e em lotes seguros.
    /// </summary>
    Task<MockGenerationResult> SeedBatchAsync(
        IDatabaseService db,
        TableMockConfig config,
        IProgress<MockDataProgress>? progress = null,
        CancellationToken ct = default);
}
