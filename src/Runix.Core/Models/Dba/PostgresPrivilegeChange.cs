namespace Runix.Core.Models.Dba;

/// <summary>
/// Representa uma alteração individual pendente de privilégio (staging) a ser aplicada via GRANT ou REVOKE.
/// </summary>
public class PostgresPrivilegeChange
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Privilege { get; set; } = string.Empty;
    public bool IsGrant { get; set; }

    public string FullTableName => string.IsNullOrEmpty(SchemaName) ? TableName : $"{SchemaName}.{TableName}";

    public string ActionDescription => IsGrant
        ? $"GRANT {Privilege} ON {FullTableName} TO {RoleName}"
        : $"REVOKE {Privilege} ON {FullTableName} FROM {RoleName}";
}
