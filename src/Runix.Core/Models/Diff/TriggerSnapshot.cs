namespace Runix.Core.Models.Diff;

/// <summary>
/// Fotografia estrutural de um Trigger do banco de dados.
/// </summary>
public class TriggerSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "public";
    public string TargetTableName { get; set; } = string.Empty;
    public string Timing { get; set; } = "AFTER"; // BEFORE | AFTER | INSTEAD OF
    public string Event { get; set; } = "INSERT"; // INSERT | UPDATE | DELETE
    public string DefinitionDdl { get; set; } = string.Empty;
}
