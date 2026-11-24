using System;

namespace Runix.Core.Models.Migrations;

/// <summary>
/// Represents a database migration script file and its applied status in the database.
/// </summary>
public record MigrationItem(
    int? InstalledRank,
    string Version,
    string Description,
    string ScriptFileName,
    string Checksum,
    DateTime? InstalledOn,
    int? ExecutionTimeMs,
    bool Success,
    MigrationStatus Status,
    string FilePath,
    string? SqlContent = null
);
