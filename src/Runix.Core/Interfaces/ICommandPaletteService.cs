using System.Collections.Generic;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface ICommandPaletteService
{
    void SetCatalog(IEnumerable<CommandPaletteItem> items);

    void RebuildCatalog(
        IEnumerable<CommandPaletteItem> systemActions,
        IEnumerable<TableInfo>? tables = null,
        IEnumerable<DatabaseObjectInfo>? programmableObjects = null,
        IEnumerable<SavedQuery>? savedQueries = null,
        IEnumerable<CustomSnippet>? snippets = null,
        ISet<string>? favoriteTables = null);

    void RecordUsage(string itemId);

    IReadOnlyList<CommandPaletteItem> Search(string? rawQuery, int maxResults = 35);
}
