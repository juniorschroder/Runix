namespace Runix.Core.Interfaces;

/// <summary>
/// Service that inspects and applies an automatic limit to SELECT queries that lack an explicit LIMIT or FETCH FIRST clause.
/// </summary>
public interface ISqlAutoLimitService
{
    /// <summary>
    /// Applies an auto-limit to a SQL statement if it is a SELECT query without an existing LIMIT or FETCH FIRST clause.
    /// Returns the modified query and a boolean indicating whether the limit was applied.
    /// </summary>
    (string Sql, bool Applied) ApplyAutoLimit(string sql, int limit);
}
