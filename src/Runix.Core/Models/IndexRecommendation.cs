using System.Collections.Generic;

namespace Runix.Core.Models;

/// <summary>
/// Representa uma recomendação automática de índice baseada em gargalos de varredura sequencial.
/// </summary>
public class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public string CreateIndexSql { get; set; } = string.Empty;
    public string SuggestedIndexName { get; set; } = string.Empty;
    public double EstimatedImpactScore { get; set; } = 80.0;

    public string ColumnsFormatted => string.Join(", ", Columns);
}
