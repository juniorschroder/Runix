using System;
using System.Collections.Generic;

namespace Runix.Core.Models;

public enum WorkspaceTabType
{
    Query,
    TableData,
    TableStructure,
    Erd,
    QueryBuilder,
    GlobalSearch,
    Health,
    Backup,
    Dba,
    Diff,
    ObjectViewer,
    Migrations
}

public class WorkspaceTabSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Query";
    public WorkspaceTabType TabType { get; set; } = WorkspaceTabType.Query;

    // SQL Query Tab state
    public string? SqlText { get; set; }
    public int CaretOffset { get; set; }
    public double VerticalScrollOffset { get; set; }
    public double HorizontalScrollOffset { get; set; }

    // Table Data / Structure Tab state
    public string? TableName { get; set; }
    public string? FilterText { get; set; }
    public int PageNumber { get; set; } = 1;

    // Generic metadata tag (e.g. "erd", "data:users", etc.)
    public string? Tag { get; set; }
}

public class WorkspaceSessionState
{
    public string ConnectionKey { get; set; } = string.Empty;
    public int ActiveTabIndex { get; set; }
    public List<WorkspaceTabSession> Tabs { get; set; } = new();
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;
}
