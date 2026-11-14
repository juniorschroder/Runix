namespace Runix.Core.Interfaces;

public class SqlFormatOptions
{
    public bool UppercaseKeywords { get; set; } = true;
    public int IndentSpaces { get; set; } = 4;
}

public interface ISqlFormatterService
{
    string Format(string sql, SqlFormatOptions? options = null);
}
