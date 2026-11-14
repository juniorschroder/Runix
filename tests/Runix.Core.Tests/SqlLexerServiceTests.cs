namespace Runix.Core.Tests;

using Runix.Core.Services;
using Xunit;

public class SqlLexerServiceTests
{
    private readonly SqlLexerService _lexer = new();

    [Fact]
    public void AnalyzeContext_SimpleSelect_ExtractsKeywordAndCurrentWord()
    {
        string sql = "SELECT nam";
        var context = _lexer.AnalyzeContext(sql, sql.Length);

        Assert.Equal("nam", context.CurrentWord);
        Assert.False(context.IsAfterDot);
        Assert.Null(context.PrecedingIdentifier);
        Assert.Equal("SELECT", context.ActiveClause);
    }

    [Fact]
    public void AnalyzeContext_AfterDot_ExtractsPrecedingIdentifier()
    {
        string sql = "SELECT c.na FROM cheeses c";
        int caret = "SELECT c.na".Length;

        var context = _lexer.AnalyzeContext(sql, caret);

        Assert.Equal("na", context.CurrentWord);
        Assert.True(context.IsAfterDot);
        Assert.Equal("c", context.PrecedingIdentifier);
        Assert.True(context.TableAliases.ContainsKey("c"));
        Assert.Equal("cheeses", context.TableAliases["c"]);
    }

    [Fact]
    public void AnalyzeContext_AfterDotWithoutCurrentWord_ExtractsPrecedingIdentifier()
    {
        string sql = "SELECT c. FROM cheeses c";
        int caret = "SELECT c.".Length;

        var context = _lexer.AnalyzeContext(sql, caret);

        Assert.Equal("", context.CurrentWord);
        Assert.True(context.IsAfterDot);
        Assert.Equal("c", context.PrecedingIdentifier);
        Assert.Equal("cheeses", context.TableAliases["c"]);
    }

    [Fact]
    public void AnalyzeContext_JoinQuery_ResolvesMultipleAliases()
    {
        string sql = "SELECT * FROM cheeses c JOIN producers p ON c.producer_id = p.id WHERE p.country";
        var context = _lexer.AnalyzeContext(sql, sql.Length);

        Assert.Equal("country", context.CurrentWord);
        Assert.True(context.IsAfterDot);
        Assert.Equal("p", context.PrecedingIdentifier);
        Assert.Equal("WHERE", context.ActiveClause);

        Assert.Equal("cheeses", context.TableAliases["c"]);
        Assert.Equal("producers", context.TableAliases["p"]);
        Assert.Contains("cheeses", context.ReferencedTables);
        Assert.Contains("producers", context.ReferencedTables);
    }

    [Fact]
    public void AnalyzeContext_EmptyOrWhitespace_ReturnsSafeDefaultContext()
    {
        var context1 = _lexer.AnalyzeContext("", 0);
        Assert.Empty(context1.CurrentWord);
        Assert.False(context1.IsAfterDot);

        var context2 = _lexer.AnalyzeContext("   \n\t  ", 4);
        Assert.Empty(context2.CurrentWord);
        Assert.False(context2.IsAfterDot);
    }

    [Fact]
    public void AnalyzeContext_InsideFromClause_IdentifiesFromClause()
    {
        string sql = "SELECT id FROM ch";
        var context = _lexer.AnalyzeContext(sql, sql.Length);

        Assert.Equal("ch", context.CurrentWord);
        Assert.Equal("FROM", context.ActiveClause);
        Assert.False(context.IsAfterDot);
    }
}
