namespace Runix.Core.Models;

public enum SqlSafetyRisk
{
    Safe,
    Warning,
    BlockedReadOnly,
    BlockedDangerous
}

public class SqlSafetyValidationResult
{
    public bool IsAllowed { get; set; } = true;
    public bool IsValid => IsAllowed;
    public SqlSafetyRisk Risk { get; set; } = SqlSafetyRisk.Safe;
    public string? Reason { get; set; }
    public string? BlockedReason => Reason;
    public string? OffendingStatement { get; set; }

    public static SqlSafetyValidationResult Safe() => new()
    {
        IsAllowed = true,
        Risk = SqlSafetyRisk.Safe
    };

    public static SqlSafetyValidationResult BlockReadOnly(string reason, string? statement = null) => new()
    {
        IsAllowed = false,
        Risk = SqlSafetyRisk.BlockedReadOnly,
        Reason = reason,
        OffendingStatement = statement
    };

    public static SqlSafetyValidationResult BlockDangerous(string reason, string? statement = null) => new()
    {
        IsAllowed = false,
        Risk = SqlSafetyRisk.BlockedDangerous,
        Reason = reason,
        OffendingStatement = statement
    };
}
