namespace Runix.Core.Services;

using System.Collections.Generic;
using Runix.Core.Models;

public interface ISqlCompletionService
{
    IReadOnlyList<CompletionItem> GetCompletions(
        SqlContextInfo context,
        IReadOnlyList<TableInfo> tables,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>>? tableColumns = null,
        IReadOnlyList<CustomSnippet>? customSnippets = null);
}
