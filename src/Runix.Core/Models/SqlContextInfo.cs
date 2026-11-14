namespace Runix.Core.Models;

using System;
using System.Collections.Generic;

public sealed class SqlContextInfo
{
    public string CurrentWord { get; init; } = string.Empty;
    public string? PrecedingIdentifier { get; init; }
    public bool IsAfterDot => !string.IsNullOrEmpty(PrecedingIdentifier);
    public string? ActiveClause { get; init; }
    public Dictionary<string, string> TableAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ReferencedTables { get; init; } = new();
}
