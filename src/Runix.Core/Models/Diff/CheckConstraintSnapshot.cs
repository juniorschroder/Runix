namespace Runix.Core.Models.Diff;

/// <summary>
/// Fotografia de uma Check Constraint de uma tabela.
/// </summary>
public class CheckConstraintSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
}
