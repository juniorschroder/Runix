using System;
using System.Collections.Generic;

namespace Runix.Core.Models.Dba;

/// <summary>
/// Representa as informações e atributos de uma Role / Usuário no PostgreSQL (pg_roles).
/// </summary>
public class PostgresRoleInfo
{
    public string RoleName { get; set; } = string.Empty;
    public bool IsSuperuser { get; set; }
    public bool InheritsPrivileges { get; set; } = true;
    public bool CanCreateRole { get; set; }
    public bool CanCreateDb { get; set; }
    public bool CanLogin { get; set; } = true;
    public bool IsReplication { get; set; }
    public int ConnectionLimit { get; set; } = -1;
    public DateTime? ValidUntil { get; set; }
    public IReadOnlyList<string> ConfigOptions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MemberOf { get; set; } = Array.Empty<string>();

    public bool IsExpired => ValidUntil.HasValue && ValidUntil.Value < DateTime.UtcNow;

    public string RoleTypeBadge
    {
        get
        {
            if (IsSuperuser) return "SUPERUSER";
            if (CanLogin) return "USER";
            return "ROLE";
        }
    }

    public string ValidUntilFormatted => ValidUntil.HasValue 
        ? ValidUntil.Value.ToString("yyyy-MM-dd HH:mm") 
        : "Nunca expira";
}
