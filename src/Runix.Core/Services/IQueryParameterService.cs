using System.Collections.Generic;

namespace Runix.Core.Services;

/// <summary>
/// Service responsible for parsing dynamic query placeholders (:param, {{param}}),
/// formatting parameter values safely into SQL literals, and managing recent parameter history.
/// </summary>
public interface IQueryParameterService
{
    /// <summary>
    /// Extracts unique parameter names found in the given SQL text in their order of appearance,
    /// ignoring comments and string literals, and excluding PostgreSQL type casts (::type).
    /// </summary>
    IReadOnlyList<string> ExtractParameters(string sql);

    /// <summary>
    /// Checks whether the SQL text contains any dynamic query parameters.
    /// </summary>
    bool HasParameters(string sql);

    /// <summary>
    /// Safely substitutes all placeholders (:param and {{param}}) in the SQL with the provided values.
    /// </summary>
    string SubstituteParameters(string sql, IReadOnlyDictionary<string, string> parameterValues);

    /// <summary>
    /// Formats a raw user input string into a safe SQL literal (handling numbers, booleans, NULLs, and escaping strings).
    /// </summary>
    string FormatSqlLiteral(string rawValue);

    /// <summary>
    /// Retrieves recent history values used for a given parameter name.
    /// </summary>
    IReadOnlyList<string> GetHistory(string parameterName);

    /// <summary>
    /// Records a value used for a parameter name into recent history.
    /// </summary>
    void SaveHistory(string parameterName, string value);

    /// <summary>
    /// Records multiple parameter values into history.
    /// </summary>
    void SaveHistory(IReadOnlyDictionary<string, string> parameters);
}
