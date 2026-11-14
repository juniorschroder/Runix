namespace Runix.Core.Services;

using Runix.Core.Models;

public interface ISqlLexerService
{
    SqlContextInfo AnalyzeContext(string sqlText, int caretIndex);
}
