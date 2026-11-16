namespace Runix.Core.Services;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Runix.Core.Interfaces;
using Runix.Core.Models;

public class SqlSafetyService : ISqlSafetyService
{
    private static readonly Regex DropRegex = new(
        @"^\s*DROP\s+(?:TABLE|DATABASE|SCHEMA|VIEW|INDEX|TRIGGER|FUNCTION|PROCEDURE|MATERIALIZED\s+VIEW|TYPE|EXTENSION|SEQUENCE)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TruncateRegex = new(
        @"^\s*TRUNCATE\s+(?:TABLE\s+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DeleteRegex = new(
        @"^\s*DELETE\s+FROM\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DeleteShortRegex = new(
        @"^\s*DELETE\s+[a-zA-Z0-9_""`\[\]]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UpdateRegex = new(
        @"^\s*UPDATE\s+(?:ONLY\s+)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WhereKeywordRegex = new(
        @"\bWHERE\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SqlSafetyValidationResult ValidateQuery(string sql, ConnectionConfig config)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlSafetyValidationResult.Safe();
        }

        var statements = SplitStatements(sql);

        foreach (var statement in statements)
        {
            var cleaned = StripComments(statement).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            // 1. Check Read-Only Connection Lock
            if (config.IsReadOnly)
            {
                if (!IsReadOnlyAllowed(cleaned))
                {
                    return SqlSafetyValidationResult.BlockReadOnly(
                        "🔒 Conexão bloqueada em Modo Somente-Leitura (Read-Only Connection Lock). Instruções de mutação e DDL estão desabilitadas.",
                        statement.Trim());
                }
            }

            // 2. Check Production Environment & Safe Execution Mode Safeguards
            if (config.IsProductionEnvironment || config.SafeExecutionMode)
            {
                // Rule A: Block DROP in production
                if (DropRegex.IsMatch(cleaned))
                {
                    return SqlSafetyValidationResult.BlockDangerous(
                        "🚨 [Alerta de Produção] O comando 'DROP' foi bloqueado para proteger a integridade do banco de produção.",
                        statement.Trim());
                }

                // Rule B: Block TRUNCATE in production
                if (TruncateRegex.IsMatch(cleaned))
                {
                    return SqlSafetyValidationResult.BlockDangerous(
                        "🚨 [Alerta de Produção] O comando 'TRUNCATE' foi bloqueado para evitar perda irreversível de dados no ambiente de produção.",
                        statement.Trim());
                }

                // Rule C: Block DELETE without WHERE in production
                if (DeleteRegex.IsMatch(cleaned) || DeleteShortRegex.IsMatch(cleaned))
                {
                    if (!HasWhereClause(cleaned))
                    {
                        return SqlSafetyValidationResult.BlockDangerous(
                            "🚨 [Alerta de Produção] Comando 'DELETE' sem cláusula WHERE detectado! Toda a tabela seria apagada. O comando foi bloqueado por segurança.",
                            statement.Trim());
                    }
                }

                // Rule D: Block UPDATE without WHERE in production
                if (UpdateRegex.IsMatch(cleaned))
                {
                    if (!HasWhereClause(cleaned))
                    {
                        return SqlSafetyValidationResult.BlockDangerous(
                            "🚨 [Alerta de Produção] Comando 'UPDATE' sem cláusula WHERE detectado! Todos os registros da tabela seriam modificados. O comando foi bloqueado por segurança.",
                            statement.Trim());
                    }
                }
            }
        }

        return SqlSafetyValidationResult.Safe();
    }

    private static bool IsReadOnlyAllowed(string sql)
    {
        var firstWord = GetFirstWord(sql);
        return firstWord.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
               firstWord.Equals("WITH", StringComparison.OrdinalIgnoreCase) ||
               firstWord.Equals("EXPLAIN", StringComparison.OrdinalIgnoreCase) ||
               firstWord.Equals("SHOW", StringComparison.OrdinalIgnoreCase) ||
               firstWord.Equals("DESCRIBE", StringComparison.OrdinalIgnoreCase) ||
               firstWord.Equals("DESC", StringComparison.OrdinalIgnoreCase) ||
               (firstWord.Equals("PRAGMA", StringComparison.OrdinalIgnoreCase) && !sql.Contains('='));
    }

    private static bool HasWhereClause(string sql)
    {
        // Strip string literals to ensure WHERE is not part of a text value
        var withoutStrings = StripStringLiterals(sql);
        return WhereKeywordRegex.IsMatch(withoutStrings);
    }

    private static string GetFirstWord(string sql)
    {
        var span = sql.AsSpan().TrimStart();
        int end = 0;
        while (end < span.Length && char.IsLetterOrDigit(span[end]))
        {
            end++;
        }
        return end > 0 ? span[..end].ToString() : string.Empty;
    }

    public static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        if (string.IsNullOrWhiteSpace(sql))
        {
            return statements;
        }

        int start = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        bool inDollarQuote = false;
        string dollarTag = "";

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if (!inDoubleQuote && !inDollarQuote && c == '\'')
            {
                if (inSingleQuote && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++; // escaped quote
                }
                else
                {
                    inSingleQuote = !inSingleQuote;
                }
            }
            else if (!inSingleQuote && !inDollarQuote && c == '"')
            {
                if (inDoubleQuote && i + 1 < sql.Length && sql[i + 1] == '"')
                {
                    i++; // escaped quote
                }
                else
                {
                    inDoubleQuote = !inDoubleQuote;
                }
            }
            else if (!inSingleQuote && !inDoubleQuote && c == '$' && i + 1 < sql.Length)
            {
                // Dollar quoting $$ or $tag$
                int nextDollar = sql.IndexOf('$', i + 1);
                if (nextDollar > i)
                {
                    var tag = sql.Substring(i, nextDollar - i + 1);
                    if (Regex.IsMatch(tag, @"^\$[a-zA-Z0-9_]*\$$"))
                    {
                        if (inDollarQuote && tag == dollarTag)
                        {
                            inDollarQuote = false;
                            dollarTag = "";
                            i = nextDollar;
                        }
                        else if (!inDollarQuote)
                        {
                            inDollarQuote = true;
                            dollarTag = tag;
                            i = nextDollar;
                        }
                    }
                }
            }
            else if (!inSingleQuote && !inDoubleQuote && !inDollarQuote && c == ';')
            {
                var stmt = sql.Substring(start, i - start).Trim();
                if (!string.IsNullOrEmpty(stmt))
                {
                    statements.Add(stmt);
                }
                start = i + 1;
            }
        }

        if (start < sql.Length)
        {
            var remaining = sql.Substring(start).Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                statements.Add(remaining);
            }
        }

        return statements;
    }

    public static string StripComments(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        // Line comments: --
        var withoutLineComments = Regex.Replace(sql, @"--.*?(\r?\n|$)", " ", RegexOptions.Multiline);
        // Block comments: /* ... */
        var withoutBlockComments = Regex.Replace(withoutLineComments, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return withoutBlockComments;
    }

    public static string StripStringLiterals(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        // Replace '...' with ''
        var withoutSingle = Regex.Replace(sql, @"'(?:''|[^'])*'", "''");
        // Replace $$...$$ with $$
        var withoutDollar = Regex.Replace(withoutSingle, @"\$[a-zA-Z0-9_]*\$.*?\$[a-zA-Z0-9_]*\$", "$$", RegexOptions.Singleline);
        return withoutDollar;
    }
}
