namespace Runix.Core.Models;

/// <summary>
/// Mapeamento de uma coluna do arquivo de entrada para uma coluna da tabela no banco de dados.
/// </summary>
public class ColumnMapping
{
    public string SourceColumn { get; set; } = string.Empty;
    public string? TargetColumn { get; set; }
    public string? TargetDataType { get; set; }
    public bool IsIgnored => string.IsNullOrWhiteSpace(TargetColumn);
    public object? DefaultValue { get; set; }

    public override string ToString() => IsIgnored
        ? $"{SourceColumn} -> <Ignorar>"
        : $"{SourceColumn} -> {TargetColumn} ({TargetDataType})";
}
