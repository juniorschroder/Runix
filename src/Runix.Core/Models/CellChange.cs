namespace Runix.Core.Models;

public sealed class CellChange
{
    public required int RowIndex { get; init; }
    public required string ColumnName { get; init; }
    public object? OriginalValue { get; init; }
    public object? NewValue { get; set; }
}
