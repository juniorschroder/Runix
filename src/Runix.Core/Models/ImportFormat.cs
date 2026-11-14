namespace Runix.Core.Models;

/// <summary>
/// Formato de arquivo aceito para importação de dados.
/// </summary>
public enum ImportFileFormat
{
    Csv,
    Json,
    Excel
}

/// <summary>
/// Estratégia de tratamento de erros ao processar linhas na importação.
/// </summary>
public enum ImportErrorStrategy
{
    /// <summary>
    /// Se qualquer linha falhar, desfaz toda a importação atomicamente.
    /// </summary>
    RollbackOnError,

    /// <summary>
    /// Pula linhas que apresentarem erro e importa todas as linhas válidas, gerando relatório final.
    /// </summary>
    SkipErrorsAndContinue
}
