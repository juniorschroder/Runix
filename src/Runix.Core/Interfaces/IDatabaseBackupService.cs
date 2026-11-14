using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IDatabaseBackupService
{
    Task<BackupResult> CreateBackupAsync(
        BackupOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<BackupResult> RestoreBackupAsync(
        RestoreOptions options,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> SortTablesTopologically(
        IReadOnlyList<string> tableNames,
        IReadOnlyList<ForeignKeyInfo> foreignKeys);

    IReadOnlyList<string> SplitSqlStatements(string sqlScript);
}
