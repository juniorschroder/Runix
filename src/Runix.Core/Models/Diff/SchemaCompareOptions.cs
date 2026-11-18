namespace Runix.Core.Models.Diff;

/// <summary>
/// Opções de execução do motor de comparação de schemas.
/// </summary>
public class SchemaCompareOptions
{
    /// <summary>
    /// Se verdadeiro, exige correspondência exata de tipos de dados.
    /// Se falso (padrão tolerante), considera equivalentes mapeamentos semânticos compatíveis entre SQLite e PostgreSQL.
    /// </summary>
    public bool StrictTypeMatching { get; set; } = false;

    /// <summary>
    /// Se verdadeiro, inverte a direção de comparação (troca origem por destino).
    /// </summary>
    public bool InvertDirection { get; set; } = false;

    /// <summary>
    /// Se verdadeiro, ignora diferenças em índices não-PK.
    /// </summary>
    public bool IgnoreIndexes { get; set; } = false;
}
