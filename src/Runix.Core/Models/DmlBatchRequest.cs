namespace Runix.Core.Models;

public sealed class DmlBatchRequest
{
    public required string TableName { get; init; }
    public required IReadOnlyList<ParameterizedCommand> Commands { get; init; }
}
