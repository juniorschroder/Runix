namespace Runix.Core.Models.MockData;

/// <summary>
/// Progresso de execução de seeding em lote.
/// </summary>
public class MockDataProgress
{
    public int TotalRows { get; set; }
    public int RowsInserted { get; set; }
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
    public int Percentage { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// Resultado da operação de geração ou inserção de mock data.
/// </summary>
public class MockGenerationResult
{
    public bool Success { get; set; }
    public int RowsInserted { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GeneratedSqlScript { get; set; }
}
