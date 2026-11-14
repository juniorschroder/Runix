using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IDatabaseHealthService
{
    Task<DatabaseHealthReport> CollectHealthReportAsync(
        bool includeIntegrityCheck = false,
        CancellationToken cancellationToken = default);

    string GetMaintenanceSqlPreview(MaintenanceOperationType operation, string? tableName = null);

    Task<MaintenanceExecutionResult> ExecuteMaintenanceAsync(
        MaintenanceOperationType operation,
        string? tableName = null,
        CancellationToken cancellationToken = default);
}
