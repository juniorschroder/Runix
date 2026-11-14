namespace Runix.Core.Services;

public class PostgresDmlGeneratorService : SqliteDmlGeneratorService
{
    protected override string BuildWhereClause(
        IReadOnlyList<string> primaryKeys,
        IReadOnlyDictionary<string, object?> targetPkValues,
        Dictionary<string, object?> parameters,
        ref int paramIndex)
    {
        if (primaryKeys.Count == 0 && (targetPkValues.TryGetValue("ctid", out var ctidVal) || targetPkValues.TryGetValue("_ctid_", out ctidVal)))
        {
            string paramName = $"@p{paramIndex++}";
            parameters[paramName] = CoerceValue(ctidVal);
            return $"ctid = {paramName}::tid";
        }

        return base.BuildWhereClause(primaryKeys, targetPkValues, parameters, ref paramIndex);
    }
}
