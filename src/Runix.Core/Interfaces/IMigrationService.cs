using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models.Migrations;

namespace Runix.Core.Interfaces;

public interface IMigrationService
{
    Task<IReadOnlyList<MigrationItem>> ScanMigrationsAsync(
        IDatabaseService db,
        string migrationsDirectory,
        CancellationToken ct = default);

    Task<MigrationResult> ApplyPendingAsync(
        IDatabaseService db,
        string migrationsDirectory,
        IReadOnlyList<MigrationItem> pending,
        CancellationToken ct = default);

    Task<string> ExportDiffAsMigrationAsync(
        string targetDirectory,
        string description,
        string ddlContent,
        CancellationToken ct = default);
}
