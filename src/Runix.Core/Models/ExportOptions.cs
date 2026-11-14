namespace Runix.Core.Models;

/// <summary>
/// Configuração completa de parâmetros para exportação de dados.
/// </summary>
public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Csv;
    public ExportScope Scope { get; set; } = ExportScope.CurrentPage;
    public string FilePath { get; set; } = string.Empty;
    public string TableName { get; set; } = "export";
    public DatabaseType TargetDatabaseType { get; set; } = DatabaseType.SQLite;

    // Opções de CSV
    public CsvDelimiter CsvDelimiter { get; set; } = CsvDelimiter.Comma;
    public bool IncludeHeader { get; set; } = true;
    public bool UseBom { get; set; } = false;

    // Opções de JSON
    public JsonFormatStyle JsonStyle { get; set; } = JsonFormatStyle.ArrayOfObjects;
    public bool JsonIndented { get; set; } = true;

    // Opções de Excel (.xlsx)
    public string SheetName { get; set; } = "Data";

    // Opções de SQL Dump
    public bool IncludeCreateTable { get; set; } = false;
    public int BatchSize { get; set; } = 500;
    public bool IncludeTransaction { get; set; } = true;

    /// <summary>
    /// Retorna o caractere separador real a partir do enum CsvDelimiter.
    /// </summary>
    public char GetDelimiterChar() => CsvDelimiter switch
    {
        CsvDelimiter.Semicolon => ';',
        CsvDelimiter.Tab => '\t',
        CsvDelimiter.Pipe => '|',
        _ => ','
    };
}
