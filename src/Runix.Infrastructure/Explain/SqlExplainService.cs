using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models;

namespace Runix.Infrastructure.Explain;

/// <summary>
/// Serviço central de execução e análise de planos SQL para PostgreSQL e SQLite.
/// </summary>
public class SqlExplainService : ISqlExplainService
{
    public static SqlExplainService Instance { get; } = new();

    public async Task<ExecutionPlanModel> ExplainQueryAsync(
        IDatabaseService databaseService,
        string sql,
        bool analyze = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new ExecutionPlanModel
            {
                RawOutput = "Nenhuma instrução SQL fornecida para o Explain."
            };
        }

        var isPostgres = databaseService.Config.Type == DatabaseType.PostgreSQL;

        if (isPostgres)
        {
            var explainPrefix = analyze
                ? "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) "
                : "EXPLAIN (BUFFERS, FORMAT JSON) ";

            var fullSql = explainPrefix + sql.Trim().TrimEnd(';');
            var result = await databaseService.ExecuteQueryAsync(fullSql);

            if (!result.Success || result.Rows.Count == 0)
            {
                return new ExecutionPlanModel
                {
                    Dialect = "PostgreSQL",
                    RawOutput = result.ErrorMessage ?? "Falha ao obter plano de execução do PostgreSQL."
                };
            }

            // Concatenate output rows if split
            var sb = new StringBuilder();
            foreach (var row in result.Rows)
            {
                if (row.Count > 0 && row[0] != null)
                {
                    sb.Append(row[0]);
                }
            }

            var plan = ParsePostgresPlan(sb.ToString());
            plan.Recommendations = IndexRecommendationEngine.AnalyzePlan(plan, sql);
            return plan;
        }
        else
        {
            // SQLite
            var explainSql = "EXPLAIN QUERY PLAN " + sql.Trim().TrimEnd(';');
            var result = await databaseService.ExecuteQueryAsync(explainSql);

            var plan = ParseSqlitePlan(result);
            plan.Recommendations = IndexRecommendationEngine.AnalyzePlan(plan, sql);
            return plan;
        }
    }

    public ExecutionPlanModel ParsePostgresPlan(string jsonOutput)
    {
        return PostgresExplainParser.Parse(jsonOutput);
    }

    public ExecutionPlanModel ParseSqlitePlan(QueryResult queryPlanResult)
    {
        return SqliteExplainParser.Parse(queryPlanResult);
    }
}
