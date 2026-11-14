namespace Runix.Core.Services;

using Runix.Core.Models;

public interface IDmlGeneratorService
{
    ParameterizedCommand GenerateUpdate(
        string tableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues,
        IReadOnlyDictionary<string, object?> modifiedColumns);

    ParameterizedCommand GenerateInsert(
        string tableName,
        IReadOnlyDictionary<string, object?> columnValues);

    ParameterizedCommand GenerateDelete(
        string tableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues);

    DmlBatchRequest GenerateBatch(
        StagedChangeset changeset,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? originalRows = null);
}
