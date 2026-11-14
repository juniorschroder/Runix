using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de execução e parsing de planos de execução SQL (Explain e Explain Analyze).
/// </summary>
public interface ISqlExplainService
{
    Task<ExecutionPlanModel> ExplainQueryAsync(
        IDatabaseService databaseService,
        string sql,
        bool analyze = false,
        CancellationToken ct = default);

    ExecutionPlanModel ParsePostgresPlan(string jsonOutput);

    ExecutionPlanModel ParseSqlitePlan(QueryResult queryPlanResult);
}
