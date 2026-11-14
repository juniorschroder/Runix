namespace Runix.Core.Models.Diff;

/// <summary>
/// Status semântico de divergência entre a origem e o destino na comparação de schemas.
/// </summary>
public enum DiffStatus
{
    /// <summary>
    /// O objeto é idêntico em ambas as fontes.
    /// </summary>
    Identical,

    /// <summary>
    /// O objeto existe na Origem mas está ausente no Destino (Novo / A adicionar).
    /// </summary>
    Added,

    /// <summary>
    /// O objeto existe em ambos mas possui propriedades ou colunas divergentes.
    /// </summary>
    Modified,

    /// <summary>
    /// O objeto existe no Destino mas está ausente na Origem (Extra / A remover).
    /// </summary>
    Removed
}
