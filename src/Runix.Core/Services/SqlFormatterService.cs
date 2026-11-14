using System;
using System.Collections.Generic;
using System.Text;
using Runix.Core.Interfaces;

namespace Runix.Core.Services;

public class SqlFormatterService : ISqlFormatterService
{
    private enum TokenKind
    {
        Word,
        StringLiteral,
        QuotedIdentifier,
        DollarQuoted,
        LineComment,
        BlockComment,
        Operator,
        Comma,
        Semicolon,
        OpenParen,
        CloseParen,
        Dot
    }

    private readonly record struct SqlToken(TokenKind Kind, string Value);

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "DISTINCT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE", "ILIKE",
        "IS", "NULL", "TRUE", "FALSE", "AS", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON",
        "USING", "GROUP", "BY", "HAVING", "ORDER", "ASC", "DESC", "NULLS", "FIRST", "LAST", "LIMIT", "OFFSET",
        "UNION", "ALL", "INTERSECT", "EXCEPT", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "RETURNING",
        "CREATE", "OR", "REPLACE", "ALTER", "DROP", "TABLE", "VIEW", "TRIGGER", "FUNCTION", "PROCEDURE", "INDEX",
        "UNIQUE", "IF", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "DEFAULT", "CHECK", "CONSTRAINT", "CASCADE",
        "RESTRICT", "CASE", "WHEN", "THEN", "ELSE", "END", "CAST", "WITH", "RECURSIVE", "OVER", "PARTITION",
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION"
    };

    private static readonly HashSet<string> TopLevelNewlineClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "GROUP BY", "HAVING", "ORDER BY", "LIMIT", "OFFSET",
        "JOIN", "INNER JOIN", "LEFT JOIN", "LEFT OUTER JOIN", "RIGHT JOIN", "RIGHT OUTER JOIN",
        "FULL JOIN", "FULL OUTER JOIN", "CROSS JOIN",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "RETURNING"
    };

    private static readonly HashSet<string> IndentedSubClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "ON"
    };

    private static readonly HashSet<string> CommaBreakClauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "SET", "GROUP BY", "ORDER BY"
    };

    public string Format(string sql, SqlFormatOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        options ??= new SqlFormatOptions();
        var indentUnit = new string(' ', Math.Max(1, options.IndentSpaces));

        var rawTokens = Tokenize(sql);
        if (rawTokens.Count == 0)
            return string.Empty;

        var tokens = MergeCompoundClauses(rawTokens, options.UppercaseKeywords);
        var sb = new StringBuilder(sql.Length + 64);

        int parenDepth = 0;
        string currentClause = string.Empty;
        bool atLineStart = true;

        void AppendNewLine(int indentLevel = 0)
        {
            // Trim trailing spaces on current line
            while (sb.Length > 0 && sb[^1] == ' ')
                sb.Length--;

            if (sb.Length > 0 && sb[^1] != '\n')
                sb.Append('\n');

            for (int i = 0; i < indentLevel; i++)
                sb.Append(indentUnit);

            atLineStart = true;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            var prev = i > 0 ? tokens[i - 1] : (SqlToken?)null;

            if (tok.Kind == TokenKind.LineComment)
            {
                if (!atLineStart)
                    sb.Append(' ');
                sb.Append(tok.Value.TrimEnd());
                AppendNewLine(parenDepth > 0 ? 1 : (CommaBreakClauses.Contains(currentClause) ? 1 : 0));
                continue;
            }

            if (tok.Kind == TokenKind.BlockComment)
            {
                if (!atLineStart)
                    sb.Append(' ');
                sb.Append(tok.Value);
                atLineStart = false;
                continue;
            }

            if (tok.Kind == TokenKind.OpenParen)
            {
                // No space between function name and '(' unless previous token is a keyword like WHERE/IN/VALUES/ON/AND/OR
                bool spaceBeforeParen = prev.HasValue &&
                    (prev.Value.Kind == TokenKind.Operator ||
                     prev.Value.Kind == TokenKind.Comma ||
                     (prev.Value.Kind == TokenKind.Word && IsSpaceBeforeParenKeyword(prev.Value.Value)));

                if (!atLineStart && spaceBeforeParen)
                    sb.Append(' ');

                sb.Append('(');
                parenDepth++;
                atLineStart = false;
                continue;
            }

            if (tok.Kind == TokenKind.CloseParen)
            {
                parenDepth = Math.Max(0, parenDepth - 1);
                sb.Append(')');
                atLineStart = false;
                continue;
            }

            if (tok.Kind == TokenKind.Dot)
            {
                sb.Append('.');
                atLineStart = false;
                continue;
            }

            if (tok.Kind == TokenKind.Comma)
            {
                sb.Append(',');
                if (parenDepth == 0 && CommaBreakClauses.Contains(currentClause))
                {
                    AppendNewLine(1);
                }
                else
                {
                    sb.Append(' ');
                    atLineStart = false;
                }
                continue;
            }

            if (tok.Kind == TokenKind.Semicolon)
            {
                sb.Append(';');
                currentClause = string.Empty;
                if (i < tokens.Count - 1)
                {
                    AppendNewLine(0);
                    sb.Append('\n');
                    atLineStart = true;
                }
                else
                {
                    atLineStart = false;
                }
                continue;
            }

            if (tok.Kind == TokenKind.Operator)
            {
                if (tok.Value == "::")
                {
                    sb.Append("::");
                }
                else
                {
                    if (!atLineStart)
                        sb.Append(' ');
                    sb.Append(tok.Value);
                    sb.Append(' ');
                }
                atLineStart = false;
                continue;
            }

            // Word / Literal / Identifier / DollarQuoted
            if (parenDepth == 0 && tok.Kind == TokenKind.Word)
            {
                if (TopLevelNewlineClauses.Contains(tok.Value))
                {
                    currentClause = tok.Value.ToUpperInvariant();
                    if (sb.Length > 0)
                        AppendNewLine(0);

                    sb.Append(tok.Value);
                    atLineStart = false;

                    if (CommaBreakClauses.Contains(currentClause))
                    {
                        // If next token is DISTINCT or TOP, keep on same line, then break
                        if (currentClause == "SELECT" && i + 1 < tokens.Count &&
                            tokens[i + 1].Kind == TokenKind.Word &&
                            string.Equals(tokens[i + 1].Value, "DISTINCT", StringComparison.OrdinalIgnoreCase))
                        {
                            i++;
                            sb.Append(' ').Append(options.UppercaseKeywords ? "DISTINCT" : tokens[i].Value);
                        }
                        AppendNewLine(1);
                    }
                    continue;
                }

                if (IndentedSubClauses.Contains(tok.Value) &&
                    (currentClause is "WHERE" or "HAVING" || currentClause.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase)))
                {
                    AppendNewLine(1);
                    sb.Append(tok.Value);
                    atLineStart = false;
                    continue;
                }
            }

            // Normal token spacing
            if (!atLineStart && prev.HasValue &&
                prev.Value.Kind != TokenKind.OpenParen &&
                prev.Value.Kind != TokenKind.Dot &&
                !(prev.Value.Kind == TokenKind.Operator && prev.Value.Value == "::") &&
                sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n')
            {
                sb.Append(' ');
            }

            sb.Append(tok.Value);
            atLineStart = false;
        }

        return sb.ToString().Trim();
    }

    private static bool IsSpaceBeforeParenKeyword(string word)
    {
        return word.Equals("IN", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("VALUES", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("AS", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("FROM", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("EXISTS", StringComparison.OrdinalIgnoreCase) ||
               word.Equals("NOT", StringComparison.OrdinalIgnoreCase);
    }

    private static List<SqlToken> MergeCompoundClauses(List<SqlToken> raw, bool uppercaseKeywords)
    {
        var result = new List<SqlToken>(raw.Count);
        int i = 0;

        while (i < raw.Count)
        {
            var cur = raw[i];
            if (cur.Kind == TokenKind.Word)
            {
                // Try 3-word compound clauses: LEFT OUTER JOIN, RIGHT OUTER JOIN, FULL OUTER JOIN, CREATE OR REPLACE
                if (i + 2 < raw.Count && raw[i + 1].Kind == TokenKind.Word && raw[i + 2].Kind == TokenKind.Word)
                {
                    var three = $"{cur.Value} {raw[i + 1].Value} {raw[i + 2].Value}";
                    if (TopLevelNewlineClauses.Contains(three) ||
                        three.Equals("CREATE OR REPLACE", StringComparison.OrdinalIgnoreCase))
                    {
                        var formatted = uppercaseKeywords ? three.ToUpperInvariant() : three;
                        result.Add(new SqlToken(TokenKind.Word, formatted));
                        i += 3;
                        continue;
                    }
                }

                // Try 2-word compound clauses: INNER JOIN, LEFT JOIN, GROUP BY, ORDER BY, INSERT INTO, DELETE FROM, UNION ALL
                if (i + 1 < raw.Count && raw[i + 1].Kind == TokenKind.Word)
                {
                    var two = $"{cur.Value} {raw[i + 1].Value}";
                    if (TopLevelNewlineClauses.Contains(two))
                    {
                        var formatted = uppercaseKeywords ? two.ToUpperInvariant() : two;
                        result.Add(new SqlToken(TokenKind.Word, formatted));
                        i += 2;
                        continue;
                    }
                }

                var wordVal = uppercaseKeywords && SqlKeywords.Contains(cur.Value)
                    ? cur.Value.ToUpperInvariant()
                    : cur.Value;
                result.Add(new SqlToken(TokenKind.Word, wordVal));
                i++;
                continue;
            }

            result.Add(cur);
            i++;
        }

        return result;
    }

    private static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        int i = 0;
        int len = sql.Length;

        while (i < len)
        {
            char c = sql[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // 1. Single-line comment: --
            if (c == '-' && i + 1 < len && sql[i + 1] == '-')
            {
                int start = i;
                i += 2;
                while (i < len && sql[i] != '\n')
                    i++;
                tokens.Add(new SqlToken(TokenKind.LineComment, sql[start..i].TrimEnd('\r')));
                continue;
            }

            // 2. Block comment: /* ... */
            if (c == '/' && i + 1 < len && sql[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < len && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                if (i + 1 < len)
                    i += 2;
                else
                    i = len;
                tokens.Add(new SqlToken(TokenKind.BlockComment, sql[start..i]));
                continue;
            }

            // 3. Single-quoted string literal: '...'
            if (c == '\'')
            {
                int start = i;
                i++;
                while (i < len)
                {
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < len && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                tokens.Add(new SqlToken(TokenKind.StringLiteral, sql[start..i]));
                continue;
            }

            // 4. Double-quoted identifier: "..."
            if (c == '"')
            {
                int start = i;
                i++;
                while (i < len)
                {
                    if (sql[i] == '"')
                    {
                        if (i + 1 < len && sql[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    i++;
                }
                tokens.Add(new SqlToken(TokenKind.QuotedIdentifier, sql[start..i]));
                continue;
            }

            // 5. PostgreSQL Dollar-Quoted block: $$...$$ or $tag$...$tag$
            if (c == '$')
            {
                int tagEnd = i + 1;
                while (tagEnd < len && (char.IsLetterOrDigit(sql[tagEnd]) || sql[tagEnd] == '_'))
                    tagEnd++;

                if (tagEnd < len && sql[tagEnd] == '$')
                {
                    string delimiter = sql[i..(tagEnd + 1)];
                    int start = i;
                    int closePos = sql.IndexOf(delimiter, tagEnd + 1, StringComparison.Ordinal);
                    if (closePos >= 0)
                    {
                        i = closePos + delimiter.Length;
                    }
                    else
                    {
                        i = len;
                    }
                    tokens.Add(new SqlToken(TokenKind.DollarQuoted, sql[start..i]));
                    continue;
                }
            }

            // 6. Punctuation
            if (c == ',') { tokens.Add(new SqlToken(TokenKind.Comma, ",")); i++; continue; }
            if (c == ';') { tokens.Add(new SqlToken(TokenKind.Semicolon, ";")); i++; continue; }
            if (c == '(') { tokens.Add(new SqlToken(TokenKind.OpenParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new SqlToken(TokenKind.CloseParen, ")")); i++; continue; }
            if (c == '.') { tokens.Add(new SqlToken(TokenKind.Dot, ".")); i++; continue; }

            // 7. Compound & Single Operators
            if (i + 2 < len && sql[i..(i + 3)] == "->>")
            {
                tokens.Add(new SqlToken(TokenKind.Operator, "->>"));
                i += 3;
                continue;
            }

            if (i + 1 < len)
            {
                string two = sql[i..(i + 2)];
                if (two is "::" or "<>" or "!=" or "<=" or ">=" or "->" or "||")
                {
                    tokens.Add(new SqlToken(TokenKind.Operator, two));
                    i += 2;
                    continue;
                }
            }

            if (c is '=' or '<' or '>' or '+' or '-' or '*' or '/' or '%')
            {
                // Note: '*' after SELECT or ',' or '(' or '.' is a wildcard word rather than binary operator
                if (c == '*' && (tokens.Count == 0 ||
                    tokens[^1].Kind is TokenKind.OpenParen or TokenKind.Comma or TokenKind.Dot ||
                    (tokens[^1].Kind == TokenKind.Word && tokens[^1].Value.Equals("SELECT", StringComparison.OrdinalIgnoreCase))))
                {
                    tokens.Add(new SqlToken(TokenKind.Word, "*"));
                    i++;
                    continue;
                }

                tokens.Add(new SqlToken(TokenKind.Operator, c.ToString()));
                i++;
                continue;
            }

            // 8. Word / Number / Parameter (@p, :p)
            int wordStart = i;
            while (i < len && !char.IsWhiteSpace(sql[i]) &&
                   sql[i] is not (',' or ';' or '(' or ')' or '.' or '\'' or '"' or '=' or '<' or '>' or '+' or '*' or '/'))
            {
                if (sql[i] == '-' && i > wordStart)
                    break;
                if (sql[i] == ':' && i + 1 < len && sql[i + 1] == ':')
                    break;
                i++;
            }

            if (i > wordStart)
            {
                tokens.Add(new SqlToken(TokenKind.Word, sql[wordStart..i]));
            }
            else
            {
                tokens.Add(new SqlToken(TokenKind.Word, sql[i].ToString()));
                i++;
            }
        }

        return tokens;
    }
}
