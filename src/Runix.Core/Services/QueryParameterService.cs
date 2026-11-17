using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Runix.Core.Services;

/// <summary>
/// High-performance parser and substitutor for SQL dynamic query parameters (:name and {{name}}).
/// </summary>
public class QueryParameterService : IQueryParameterService
{
    private static readonly Lazy<QueryParameterService> _instance = new(() => new QueryParameterService());
    public static QueryParameterService Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, List<string>> _history = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxHistoryPerParam = 15;

    public IReadOnlyList<string> ExtractParameters(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Array.Empty<string>();

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int len = sql.Length;
        int i = 0;

        while (i < len)
        {
            char c = sql[i];

            // 1. Single-line comment: -- ...
            if (c == '-' && i + 1 < len && sql[i + 1] == '-')
            {
                i += 2;
                while (i < len && sql[i] != '\n' && sql[i] != '\r')
                    i++;
                continue;
            }

            // 2. Block comment: /* ... */
            if (c == '/' && i + 1 < len && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                i = Math.Min(i + 2, len);
                continue;
            }

            // 3. String literal: '...' (supporting SQL '' escape)
            if (c == '\'')
            {
                i++;
                while (i < len)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < len && sql[i + 1] == '\'')
                        {
                            i += 2; // escaped quote ''
                            continue;
                        }
                        i++; // closed quote
                        break;
                    }
                    i++;
                }
                continue;
            }

            // 4. Mustache placeholder: {{param_name}}
            if (c == '{' && i + 1 < len && sql[i + 1] == '{')
            {
                int start = i + 2;
                int end = sql.IndexOf("}}", start, StringComparison.Ordinal);
                if (end != -1)
                {
                    string rawName = sql.Substring(start, end - start).Trim();
                    if (IsValidIdentifier(rawName))
                    {
                        if (seen.Add(rawName))
                            results.Add(rawName);
                    }
                    i = end + 2;
                    continue;
                }
                else
                {
                    i += 2;
                    continue;
                }
            }

            // 5. Colon placeholder: :param_name (ignoring PostgreSQL cast ::type)
            if (c == ':')
            {
                // Check if preceded by colon (Postgres ::cast)
                bool precededByColon = (i > 0 && sql[i - 1] == ':');
                if (!precededByColon && i + 1 < len)
                {
                    char next = sql[i + 1];
                    // Must start with letter or underscore, not another colon
                    if (char.IsLetter(next) || next == '_')
                    {
                        int idStart = i + 1;
                        int idEnd = idStart;
                        while (idEnd < len && (char.IsLetterOrDigit(sql[idEnd]) || sql[idEnd] == '_'))
                        {
                            idEnd++;
                        }

                        // Check if immediately followed by another colon (e.g. :::)
                        bool followedByColon = (idEnd < len && sql[idEnd] == ':');
                        if (!followedByColon)
                        {
                            string paramName = sql.Substring(idStart, idEnd - idStart);
                            if (seen.Add(paramName))
                                results.Add(paramName);

                            i = idEnd;
                            continue;
                        }
                    }
                }
            }

            i++;
        }

        return results;
    }

    public bool HasParameters(string sql)
    {
        return ExtractParameters(sql).Count > 0;
    }

    public string SubstituteParameters(string sql, IReadOnlyDictionary<string, string> parameterValues)
    {
        if (string.IsNullOrWhiteSpace(sql) || parameterValues == null || parameterValues.Count == 0)
            return sql;

        string result = sql;

        foreach (var (key, rawValue) in parameterValues)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;

            string formattedValue = FormatSqlLiteral(rawValue);

            // 1. Replace {{key}} or {{ key }}
            string mustachePattern = @"\{\{\s*" + Regex.Escape(key) + @"\s*\}\}";
            result = Regex.Replace(result, mustachePattern, formattedValue, RegexOptions.IgnoreCase);

            // 2. Replace :key where not preceded by ':' (avoiding ::cast) and bounded
            string colonPattern = @"(?<!:):" + Regex.Escape(key) + @"(?![a-zA-Z0-9_])";
            result = Regex.Replace(result, colonPattern, formattedValue, RegexOptions.IgnoreCase);
        }

        return result;
    }

    public string FormatSqlLiteral(string? rawValue)
    {
        if (rawValue == null)
            return "NULL";

        string trimmed = rawValue.Trim();

        if (string.IsNullOrEmpty(trimmed))
            return "''";

        // If user explicitly wrapped in single quotes, e.g. 'hello', keep as is
        if (trimmed.Length >= 2 && trimmed.StartsWith('\'') && trimmed.EndsWith('\''))
            return trimmed;

        // Keywords: NULL, TRUE, FALSE
        if (string.Equals(trimmed, "NULL", StringComparison.OrdinalIgnoreCase))
            return "NULL";
        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase))
            return "TRUE";
        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
            return "FALSE";

        // Numeric: integer or floating point in invariant format
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
        }

        // String value: wrap in quotes and escape internal single quotes
        return $"'{trimmed.Replace("'", "''")}'";
    }

    public IReadOnlyList<string> GetHistory(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return Array.Empty<string>();

        if (_history.TryGetValue(parameterName, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }

        return Array.Empty<string>();
    }

    public void SaveHistory(string parameterName, string value)
    {
        if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(value))
            return;

        var list = _history.GetOrAdd(parameterName, _ => new List<string>());
        lock (list)
        {
            list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, value);
            if (list.Count > MaxHistoryPerParam)
            {
                list.RemoveAt(list.Count - 1);
            }
        }
    }

    public void SaveHistory(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters == null) return;
        foreach (var (k, v) in parameters)
        {
            SaveHistory(k, v);
        }
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }
        return true;
    }
}
