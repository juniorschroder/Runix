namespace Runix.Core.Models;

using System.Diagnostics.CodeAnalysis;

public sealed class ParameterizedCommand
{
    public required string Sql { get; init; }
    public Dictionary<string, object?> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ParameterizedCommand() { }

    [SetsRequiredMembers]
    public ParameterizedCommand(string sql, Dictionary<string, object?>? parameters = null)
    {
        Sql = sql;
        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                Parameters[kvp.Key] = kvp.Value;
            }
        }
    }
}
