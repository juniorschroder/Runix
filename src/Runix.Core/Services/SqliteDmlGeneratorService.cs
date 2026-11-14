namespace Runix.Core.Services;

using System.Text;
using Runix.Core.Models;

public class SqliteDmlGeneratorService : IDmlGeneratorService
{
    public virtual ParameterizedCommand GenerateUpdate(
        string tableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues,
        IReadOnlyDictionary<string, object?> modifiedColumns)
    {
        if (modifiedColumns.Count == 0)
        {
            throw new ArgumentException("No modified columns provided for UPDATE.", nameof(modifiedColumns));
        }

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append($"UPDATE \"{EscapeIdentifier(tableName)}\" SET ");

        int paramIndex = 0;
        var setClauses = new List<string>();
        foreach (var col in modifiedColumns)
        {
            string paramName = $"@p{paramIndex++}";
            setClauses.Add($"\"{EscapeIdentifier(col.Key)}\" = {paramName}");
            parameters[paramName] = CoerceValue(col.Value);
        }
        sb.Append(string.Join(", ", setClauses));

        // WHERE clause
        sb.Append(" WHERE ");
        sb.Append(BuildWhereClause(primaryKeys, targetPkValues, parameters, ref paramIndex));
        sb.Append(';');

        return new ParameterizedCommand(sb.ToString(), parameters);
    }

    public virtual ParameterizedCommand GenerateInsert(
        string tableName,
        IReadOnlyDictionary<string, object?> columnValues)
    {
        if (columnValues.Count == 0)
        {
            return new ParameterizedCommand($"INSERT INTO \"{EscapeIdentifier(tableName)}\" DEFAULT VALUES;");
        }

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        int paramIndex = 0;

        var colNames = new List<string>();
        var paramPlaceholders = new List<string>();

        foreach (var col in columnValues)
        {
            string paramName = $"@p{paramIndex++}";
            colNames.Add($"\"{EscapeIdentifier(col.Key)}\"");
            paramPlaceholders.Add(paramName);
            parameters[paramName] = CoerceValue(col.Value);
        }

        string sql = $"INSERT INTO \"{EscapeIdentifier(tableName)}\" ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", paramPlaceholders)});";
        return new ParameterizedCommand(sql, parameters);
    }

    public virtual ParameterizedCommand GenerateDelete(
        string tableName,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        int paramIndex = 0;

        var sb = new StringBuilder();
        sb.Append($"DELETE FROM \"{EscapeIdentifier(tableName)}\" WHERE ");
        sb.Append(BuildWhereClause(primaryKeys, targetPkValues, parameters, ref paramIndex));
        sb.Append(';');

        return new ParameterizedCommand(sb.ToString(), parameters);
    }

    public virtual DmlBatchRequest GenerateBatch(
        StagedChangeset changeset,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? originalRows = null)
    {
        var commands = new List<ParameterizedCommand>();

        // 1. Process Updates
        var groupedModifications = changeset.ModifiedCells
            .GroupBy(c => c.RowIndex)
            .OrderBy(g => g.Key);

        foreach (var group in groupedModifications)
        {
            int rowIndex = group.Key;
            var modifiedDict = group.ToDictionary(c => c.ColumnName, c => c.NewValue);

            IReadOnlyDictionary<string, object?> targetPkValues;
            if (originalRows != null && rowIndex >= 0 && rowIndex < originalRows.Count)
            {
                targetPkValues = originalRows[rowIndex];
            }
            else
            {
                // Fallback: extract original values from ModifiedCells
                targetPkValues = group.ToDictionary(c => c.ColumnName, c => c.OriginalValue);
            }

            commands.Add(GenerateUpdate(changeset.TableName, changeset.PrimaryKeyColumns, targetPkValues, modifiedDict));
        }

        // 2. Process Inserts
        foreach (var newRow in changeset.AddedRows)
        {
            commands.Add(GenerateInsert(changeset.TableName, newRow));
        }

        // 3. Process Deletes
        foreach (var deletedRow in changeset.DeletedRows)
        {
            commands.Add(GenerateDelete(changeset.TableName, changeset.PrimaryKeyColumns, deletedRow));
        }

        return new DmlBatchRequest
        {
            TableName = changeset.TableName,
            Commands = commands
        };
    }

    protected virtual string BuildWhereClause(
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues,
        Dictionary<string, object?> parameters,
        ref int paramIndex)
    {
        var whereClauses = new List<string>();

        if (primaryKeys.Count > 0)
        {
            foreach (var pk in primaryKeys)
            {
                string paramName = $"@p{paramIndex++}";
                whereClauses.Add($"\"{EscapeIdentifier(pk)}\" = {paramName}");
                targetPkValues.TryGetValue(pk, out var val);
                parameters[paramName] = CoerceValue(val);
            }
        }
        else if (targetPkValues.TryGetValue("_rowid_", out var rowIdVal) || targetPkValues.TryGetValue("rowid", out rowIdVal))
        {
            string paramName = $"@p{paramIndex++}";
            whereClauses.Add($"_rowid_ = {paramName}");
            parameters[paramName] = CoerceValue(rowIdVal);
        }
        else if (targetPkValues.Count > 0)
        {
            // Fallback: match all target columns
            foreach (var col in targetPkValues)
            {
                string paramName = $"@p{paramIndex++}";
                whereClauses.Add($"\"{EscapeIdentifier(col.Key)}\" = {paramName}");
                parameters[paramName] = CoerceValue(col.Value);
            }
        }
        else
        {
            throw new InvalidOperationException("Cannot target row for DML operation without primary key, _rowid_, or target row values.");
        }

        return string.Join(" AND ", whereClauses);
    }

    protected static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");

    protected static object CoerceValue(object? value)
    {
        if (value == null) return DBNull.Value;
        return value;
    }
}
