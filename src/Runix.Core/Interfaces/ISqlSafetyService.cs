namespace Runix.Core.Interfaces;

using Runix.Core.Models;

public interface ISqlSafetyService
{
    SqlSafetyValidationResult ValidateQuery(string sql, ConnectionConfig config);
}
