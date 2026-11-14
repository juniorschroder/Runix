using System.Collections.Generic;
using System.Threading.Tasks;
using Runix.Core.Models;
using Runix.Core.Models.Dba;

namespace Runix.Core.Interfaces;

/// <summary>
/// Contrato de operações administrativas específicas do PostgreSQL (Roles, Privilégios e Sessões ativas).
/// </summary>
public interface IPostgresAdminService
{
    /// <summary>
    /// Consulta o catálogo pg_roles e retorna a lista de todas as roles/usuários e seus privilégios de sistema.
    /// </summary>
    Task<IReadOnlyList<PostgresRoleInfo>> GetRolesAsync(IDatabaseService db);

    /// <summary>
    /// Gera a instrução DDL CREATE ROLE com escaping seguro.
    /// </summary>
    string GenerateCreateRoleDdl(PostgresRoleInfo role, string? password);

    /// <summary>
    /// Gera a instrução DDL ALTER ROLE para modificar permissões, senha e validade.
    /// </summary>
    string GenerateAlterRoleDdl(PostgresRoleInfo role, string? newPassword);

    /// <summary>
    /// Gera a instrução DDL DROP ROLE com escape de identificador.
    /// </summary>
    string GenerateDropRoleDdl(string roleName);

    /// <summary>
    /// Cria uma nova role no cluster PostgreSQL.
    /// </summary>
    Task<QueryResult> CreateRoleAsync(IDatabaseService db, PostgresRoleInfo role, string? password);

    /// <summary>
    /// Atualiza atributos, senha ou expiração de uma role existente.
    /// </summary>
    Task<QueryResult> AlterRoleAsync(IDatabaseService db, PostgresRoleInfo role, string? newPassword);

    /// <summary>
    /// Remove uma role do cluster PostgreSQL.
    /// </summary>
    Task<QueryResult> DropRoleAsync(IDatabaseService db, string roleName);

    /// <summary>
    /// Consulta information_schema.table_privileges para listar as permissões de uma role nas tabelas.
    /// </summary>
    Task<IReadOnlyList<PostgresTablePrivilege>> GetTablePrivilegesAsync(IDatabaseService db, string roleName, string? schemaName = null);

    /// <summary>
    /// Gera os comandos SQL agrupados de GRANT e REVOKE correspondentes às alterações pendentes.
    /// </summary>
    string GeneratePrivilegeDdl(IReadOnlyList<PostgresPrivilegeChange> changes);

    /// <summary>
    /// Executa as alterações de privilégios em uma transação atômica.
    /// </summary>
    Task<QueryResult> ApplyPrivilegeChangesAsync(IDatabaseService db, IReadOnlyList<PostgresPrivilegeChange> changes);

    /// <summary>
    /// Consulta pg_stat_activity para monitorar conexões e processos ativos, identificando a sessão atual do RUNIX.
    /// </summary>
    Task<IReadOnlyList<PostgresActiveSession>> GetActiveSessionsAsync(IDatabaseService db);

    /// <summary>
    /// Cancela a consulta em execução de um backend sem fechar a conexão do cliente (pg_cancel_backend).
    /// </summary>
    Task<bool> CancelQueryAsync(IDatabaseService db, int pid);

    /// <summary>
    /// Encerra forçadamente o processo do backend e fecha a conexão do cliente (pg_terminate_backend).
    /// </summary>
    Task<bool> TerminateBackendAsync(IDatabaseService db, int pid);
}
