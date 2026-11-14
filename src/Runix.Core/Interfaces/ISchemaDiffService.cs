using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Models.Diff;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato de extração de snapshots estruturais, cálculo de diff e geração de DDL de sincronização.
/// </summary>
public interface ISchemaDiffService
{
    /// <summary>
    /// Extrai o snapshot estrutural completo (tabelas, colunas, PKs, FKs, índices) da conexão ativa.
    /// </summary>
    Task<SchemaSnapshotModel> ExtractSnapshotAsync(IDatabaseService db);

    /// <summary>
    /// Serializa um snapshot estrutural para JSON formatado.
    /// </summary>
    string SerializeSnapshot(SchemaSnapshotModel snapshot);

    /// <summary>
    /// Deserializa um snapshot estrutural a partir de JSON.
    /// </summary>
    SchemaSnapshotModel DeserializeSnapshot(string json);

    /// <summary>
    /// Compara dois snapshots e gera o relatório diferencial com status semânticos.
    /// </summary>
    SchemaDiffResult CompareSnapshots(SchemaSnapshotModel source, SchemaSnapshotModel target, bool invertDirection = false);

    /// <summary>
    /// Sintetiza o script DDL de sincronização para o dialeto alvo respeitando as opções e o SafeMode.
    /// </summary>
    string GenerateSyncDdl(SchemaDiffResult diffResult, SyncOptions options);

    /// <summary>
    /// Executa o script de sincronização no banco alvo através de transação atômica.
    /// </summary>
    Task<QueryResult> ApplySyncDdlAsync(IDatabaseService targetDb, string ddlScript);
}
