using System.Collections.Generic;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IVisualQueryBuilderService
{
    string BuildSql(VisualQueryModel model, DatabaseType dialect = DatabaseType.SQLite);

    VisualJoinClause SuggestJoinClause(
        string baseTable,
        string baseAlias,
        string joinTable,
        string joinAlias,
        IReadOnlyList<ForeignKeyInfo> foreignKeys);
}
