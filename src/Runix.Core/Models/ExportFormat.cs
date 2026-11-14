namespace Runix.Core.Models;

/// <summary>
/// Formato de arquivo para exportação de dados tabulares.
/// </summary>
public enum ExportFormat
{
    Csv,
    Json,
    Excel,
    SqlDump
}

/// <summary>
/// Escopo da exportação: página atual da grade ou conjunto completo de dados via streaming.
/// </summary>
public enum ExportScope
{
    CurrentPage,
    FullQueryOrTable
}

/// <summary>
/// Estilo de formatação de saída para arquivos JSON.
/// </summary>
public enum JsonFormatStyle
{
    /// <summary>
    /// Array de objetos: [ { "col1": "val1", ... }, ... ]
    /// </summary>
    ArrayOfObjects,

    /// <summary>
    /// Formato tabular compacto: { "columns": [ ... ], "rows": [ [ ... ], ... ] }
    /// </summary>
    Tabular
}

/// <summary>
/// Caractere separador/delimitador de colunas em exportações de texto delimitado.
/// </summary>
public enum CsvDelimiter
{
    Comma,      // ,
    Semicolon,  // ;
    Tab,        // \t
    Pipe        // |
}
