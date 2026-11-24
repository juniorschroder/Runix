using System.Collections.Generic;

namespace Runix.Core.Models.Migrations;

public record MigrationExecutionReport(
    string Version,
    string ScriptFileName,
    bool Success,
    long ExecutionTimeMs,
    string? ErrorMessage = null
);

public record MigrationResult(
    bool Success,
    int AppliedCount,
    IReadOnlyList<MigrationExecutionReport> Reports,
    string? ErrorMessage = null
);
