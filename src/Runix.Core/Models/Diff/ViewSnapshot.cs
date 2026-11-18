namespace Runix.Core.Models.Diff;

/// <summary>
/// Fotografia estrutural de uma View do banco de dados.
/// </summary>
public class ViewSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string DefinitionDdl { get; set; } = string.Empty;
    public string? Comment { get; set; }

    public string FullName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}
