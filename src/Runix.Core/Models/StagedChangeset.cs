namespace Runix.Core.Models;

public sealed class StagedChangeset
{
    public required string TableName { get; init; }
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();

    public List<CellChange> ModifiedCells { get; } = new();
    public List<Dictionary<string, object?>> AddedRows { get; } = new();
    public List<Dictionary<string, object?>> DeletedRows { get; } = new();

    public int ModifiedRowsCount => ModifiedCells.Select(c => c.RowIndex).Distinct().Count();
    public int AddedRowsCount => AddedRows.Count;
    public int DeletedRowsCount => DeletedRows.Count;

    public int TotalChangesCount => ModifiedRowsCount + AddedRowsCount + DeletedRowsCount;

    public bool HasChanges => TotalChangesCount > 0;

    public void TrackCellEdit(int rowIndex, string column, object? originalValue, object? newValue)
    {
        var existing = ModifiedCells.FirstOrDefault(c => c.RowIndex == rowIndex && string.Equals(c.ColumnName, column, StringComparison.OrdinalIgnoreCase));

        // If new value equals original value, cell is back to clean state
        if (Equals(originalValue, newValue))
        {
            if (existing != null)
            {
                ModifiedCells.Remove(existing);
            }
            return;
        }

        if (existing != null)
        {
            existing.NewValue = newValue;
        }
        else
        {
            ModifiedCells.Add(new CellChange
            {
                RowIndex = rowIndex,
                ColumnName = column,
                OriginalValue = originalValue,
                NewValue = newValue
            });
        }
    }

    public void AddRow(Dictionary<string, object?> rowValues)
    {
        AddedRows.Add(new Dictionary<string, object?>(rowValues, StringComparer.OrdinalIgnoreCase));
    }

    public void MarkRowDeleted(Dictionary<string, object?> rowValues)
    {
        DeletedRows.Add(new Dictionary<string, object?>(rowValues, StringComparer.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        ModifiedCells.Clear();
        AddedRows.Clear();
        DeletedRows.Clear();
    }
}
