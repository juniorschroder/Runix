using System.Collections.Generic;

namespace Runix.Core.Services;

/// <summary>
/// Service responsible for parsing inline column filters (operators >, <, LIKE, IS NULL, etc.)
/// and constructing SQL WHERE predicates and multi-column ORDER BY clauses.
/// </summary>
public interface IColumnFilterService
{
    /// <summary>
    /// Parses a single column's raw filter input and produces a SQL predicate.
    /// Supports >, >=, <, <=, =, <>, !=, LIKE, ILIKE, NOT LIKE, IS NULL, IS NOT NULL, IN, BETWEEN,
    /// or automatic wildcard search for plain text/numbers.
    /// </summary>
    string? BuildPredicate(string columnName, string rawInput);

    /// <summary>
    /// Combines multiple column filters into a composite SQL WHERE clause joined with AND.
    /// </summary>
    string BuildWhereClause(IReadOnlyDictionary<string, string> columnFilters);

    /// <summary>
    /// Constructs a multi-column SQL ORDER BY clause from ordered (Column, Ascending) specifications.
    /// </summary>
    string BuildOrderByClause(IReadOnlyList<(string Column, bool Ascending)> sortOrders);

    /// <summary>
    /// Evaluates if a cell value matches a filter expression in memory.
    /// Supports >, >=, <, <=, =, <>, !=, LIKE, ILIKE, NOT LIKE, IS NULL, IS NOT NULL, numbers, and substring matching.
    /// </summary>
    bool MatchesValue(object? cellValue, string rawInput);
}
