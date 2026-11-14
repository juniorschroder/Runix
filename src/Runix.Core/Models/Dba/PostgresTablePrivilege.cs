namespace Runix.Core.Models.Dba;

/// <summary>
/// Representa o estado atual de privilégios de uma role sobre uma tabela específica no PostgreSQL.
/// </summary>
public class PostgresTablePrivilege
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;

    public bool HasSelect { get; set; }
    public bool HasInsert { get; set; }
    public bool HasUpdate { get; set; }
    public bool HasDelete { get; set; }
    public bool HasTruncate { get; set; }
    public bool HasReferences { get; set; }
    public bool HasTrigger { get; set; }

    public string FullTableName => string.IsNullOrEmpty(SchemaName) ? TableName : $"{SchemaName}.{TableName}";

    public bool HasPrivilege(string privilege)
    {
        return privilege.ToUpperInvariant() switch
        {
            "SELECT" => HasSelect,
            "INSERT" => HasInsert,
            "UPDATE" => HasUpdate,
            "DELETE" => HasDelete,
            "TRUNCATE" => HasTruncate,
            "REFERENCES" => HasReferences,
            "TRIGGER" => HasTrigger,
            _ => false
        };
    }

    public void SetPrivilege(string privilege, bool value)
    {
        switch (privilege.ToUpperInvariant())
        {
            case "SELECT": HasSelect = value; break;
            case "INSERT": HasInsert = value; break;
            case "UPDATE": HasUpdate = value; break;
            case "DELETE": HasDelete = value; break;
            case "TRUNCATE": HasTruncate = value; break;
            case "REFERENCES": HasReferences = value; break;
            case "TRIGGER": HasTrigger = value; break;
        }
    }

    public PostgresTablePrivilege Clone()
    {
        return new PostgresTablePrivilege
        {
            SchemaName = SchemaName,
            TableName = TableName,
            RoleName = RoleName,
            HasSelect = HasSelect,
            HasInsert = HasInsert,
            HasUpdate = HasUpdate,
            HasDelete = HasDelete,
            HasTruncate = HasTruncate,
            HasReferences = HasReferences,
            HasTrigger = HasTrigger
        };
    }
}
