using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models.Diff;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato do serviço de serialização streaming, compressão GZip e validação criptográfica de integridade SHA-256 para snapshots (.rschema).
/// </summary>
public interface ISchemaSnapshotStorageService
{
    /// <summary>
    /// Serializa e grava o snapshot em streaming assíncrono para o arquivo de destino,
    /// com compressão GZip e envelope de integridade SHA-256 via atomic write-and-swap.
    /// </summary>
    Task SaveSnapshotAsync(
        string filePath,
        SchemaSnapshotModel snapshot,
        SnapshotMetadata? metadata = null,
        bool compressGzip = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Carrega o snapshot de um arquivo .rschema ou .json, detectando automaticamente GZip (0x1F 0x8B)
    /// e validando o checksum SHA-256. Se o checksum for inválido, lança SnapshotIntegrityException
    /// a menos que allowCompromised seja true.
    /// </summary>
    Task<SchemaSnapshotEnvelope> LoadSnapshotAsync(
        string filePath,
        bool allowCompromised = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Carrega um snapshot a partir de uma Stream genérica (útil para testes em memória).
    /// </summary>
    Task<SchemaSnapshotEnvelope> LoadSnapshotAsync(
        Stream stream,
        bool allowCompromised = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calcula o checksum canônico SHA-256 do payload do snapshot.
    /// </summary>
    string ComputeChecksum(SchemaSnapshotModel snapshot);

    /// <summary>
    /// Valida o checksum do envelope contra o snapshot carregado usando comparação em tempo constante.
    /// </summary>
    bool VerifyChecksum(SchemaSnapshotEnvelope envelope, out string calculatedHash);
}
