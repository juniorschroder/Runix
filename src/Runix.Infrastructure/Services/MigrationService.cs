using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Interfaces;
using Runix.Core.Models.Migrations;
using Runix.Infrastructure.Migrations;

namespace Runix.Infrastructure.Services;

public class MigrationService : IMigrationService
{
    private static readonly Regex MigrationFileRegex = new(
        @"^V(?<version>[0-9]+(?:\.[0-9]+)*)__(?<desc>.+)\.sql$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string ComputeNormalizedChecksum(string sqlContent)
    {
        if (string.IsNullOrEmpty(sqlContent))
        {
            return "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // Empty hash
        }

        string normalized = sqlContent.Replace("\r\n", "\n").Trim();
        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<IReadOnlyList<MigrationItem>> ScanMigrationsAsync(
        IDatabaseService db,
        string migrationsDirectory,
        CancellationToken ct = default)
    {
        if (!db.IsConnected)
        {
            await db.ConnectAsync();
        }

        var items = new List<MigrationItem>();

        // 1. Ensure audit table exists in the target database
        string createDdl = MigrationAuditTableHelper.GetCreateTableDdl(db.Config.Type);
        await db.ExecuteQueryAsync(createDdl, cancellationToken: ct);

        // 2. Fetch recorded migrations history from database
        string selectHistorySql = MigrationAuditTableHelper.GetSelectHistoryQuery(db.Config.Type);
        var historyResult = await db.ExecuteQueryAsync(selectHistorySql, cancellationToken: ct);

        var appliedMap = new Dictionary<string, (int rank, string checksum, DateTime installedOn, int duration, bool success)>(StringComparer.OrdinalIgnoreCase);
        if (historyResult.Success && historyResult.Rows != null)
        {
            foreach (var row in historyResult.Rows)
            {
                if (row.Count >= 10)
                {
                    int rank = Convert.ToInt32(row[0]);
                    string ver = row[1]?.ToString() ?? "";
                    string checksum = row[5]?.ToString() ?? "";
                    DateTime installedOn = DateTime.TryParse(row[7]?.ToString(), out var dt) ? dt : DateTime.UtcNow;
                    int duration = int.TryParse(row[8]?.ToString(), out var dur) ? dur : 0;
                    bool success = row[9] switch
                    {
                        bool b => b,
                        int i => i == 1,
                        long l => l == 1,
                        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
                        _ => true
                    };

                    if (!string.IsNullOrEmpty(ver))
                    {
                        appliedMap[ver] = (rank, checksum, installedOn, duration, success);
                    }
                }
            }
        }

        // 3. Scan disk directory for migration script files
        if (Directory.Exists(migrationsDirectory))
        {
            var files = Directory.GetFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly);
            foreach (var filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                var match = MigrationFileRegex.Match(fileName);
                if (!match.Success)
                {
                    continue; // Skip files not following V{version}__{description}.sql
                }

                string version = match.Groups["version"].Value;
                string description = match.Groups["desc"].Value.Replace('_', ' ').Trim();
                string sqlContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
                string diskChecksum = ComputeNormalizedChecksum(sqlContent);

                if (appliedMap.TryGetValue(version, out var applied))
                {
                    bool isMismatch = !string.Equals(diskChecksum, applied.checksum, StringComparison.OrdinalIgnoreCase);
                    items.Add(new MigrationItem(
                        InstalledRank: applied.rank,
                        Version: version,
                        Description: description,
                        ScriptFileName: fileName,
                        Checksum: diskChecksum,
                        InstalledOn: applied.installedOn,
                        ExecutionTimeMs: applied.duration,
                        Success: applied.success,
                        Status: isMismatch ? MigrationStatus.MismatchChecksum : (applied.success ? MigrationStatus.Applied : MigrationStatus.Failed),
                        FilePath: filePath,
                        SqlContent: sqlContent
                    ));

                    // Remove from appliedMap so we know what remains
                    appliedMap.Remove(version);
                }
                else
                {
                    items.Add(new MigrationItem(
                        InstalledRank: null,
                        Version: version,
                        Description: description,
                        ScriptFileName: fileName,
                        Checksum: diskChecksum,
                        InstalledOn: null,
                        ExecutionTimeMs: null,
                        Success: false,
                        Status: MigrationStatus.Pending,
                        FilePath: filePath,
                        SqlContent: sqlContent
                    ));
                }
            }
        }

        // 4. Include any database migrations that are recorded in the DB but missing from disk
        foreach (var kvp in appliedMap)
        {
            items.Add(new MigrationItem(
                InstalledRank: kvp.Value.rank,
                Version: kvp.Key,
                Description: "Missing on disk",
                ScriptFileName: $"V{kvp.Key}__unknown.sql",
                Checksum: kvp.Value.checksum,
                InstalledOn: kvp.Value.installedOn,
                ExecutionTimeMs: kvp.Value.duration,
                Success: kvp.Value.success,
                Status: MigrationStatus.Applied,
                FilePath: "",
                SqlContent: "-- Script file not found in local directory."
            ));
        }

        // 5. Sort natural semantic version ordering
        items.Sort((a, b) => MigrationVersionComparer.Instance.Compare(a.Version, b.Version));
        return items;
    }

    public async Task<MigrationResult> ApplyPendingAsync(
        IDatabaseService db,
        string migrationsDirectory,
        IReadOnlyList<MigrationItem> pending,
        CancellationToken ct = default)
    {
        if (!db.IsConnected)
        {
            await db.ConnectAsync();
        }

        // 1. Rescan first to ensure no tamper drift has occurred
        var currentScan = await ScanMigrationsAsync(db, migrationsDirectory, ct);
        var tampered = currentScan.Where(m => m.Status == MigrationStatus.MismatchChecksum).ToList();
        if (tampered.Count > 0)
        {
            string files = string.Join(", ", tampered.Select(t => t.ScriptFileName));
            return new MigrationResult(
                Success: false,
                AppliedCount: 0,
                Reports: Array.Empty<MigrationExecutionReport>(),
                ErrorMessage: $"INTEGRITY CHECK FAILED: The following applied migration(s) have been modified on disk: {files}. Applying pending migrations is blocked."
            );
        }

        var toApply = currentScan
            .Where(m => m.Status == MigrationStatus.Pending)
            .OrderBy(m => m.Version, MigrationVersionComparer.Instance)
            .ToList();

        if (toApply.Count == 0)
        {
            return new MigrationResult(
                Success: true,
                AppliedCount: 0,
                Reports: Array.Empty<MigrationExecutionReport>()
            );
        }

        // Determine current max rank
        int currentRank = currentScan
            .Where(m => m.InstalledRank.HasValue)
            .Select(m => m.InstalledRank!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var reports = new List<MigrationExecutionReport>();
        int appliedCount = 0;

        foreach (var migration in toApply)
        {
            ct.ThrowIfCancellationRequested();

            string scriptSql = migration.SqlContent ?? "";
            if (string.IsNullOrWhiteSpace(scriptSql) && File.Exists(migration.FilePath))
            {
                scriptSql = await File.ReadAllTextAsync(migration.FilePath, Encoding.UTF8, ct);
            }

            var sw = Stopwatch.StartNew();
            var execResult = await db.ExecuteQueryAsync(scriptSql, cancellationToken: ct);
            sw.Stop();

            if (!execResult.Success)
            {
                reports.Add(new MigrationExecutionReport(
                    Version: migration.Version,
                    ScriptFileName: migration.ScriptFileName,
                    Success: false,
                    ExecutionTimeMs: sw.ElapsedMilliseconds,
                    ErrorMessage: execResult.ErrorMessage
                ));

                return new MigrationResult(
                    Success: false,
                    AppliedCount: appliedCount,
                    Reports: reports,
                    ErrorMessage: $"Migration {migration.ScriptFileName} failed: {execResult.ErrorMessage}"
                );
            }

            currentRank++;
            string checksum = ComputeNormalizedChecksum(scriptSql);
            DateTime now = DateTime.UtcNow;

            string insertSql = MigrationAuditTableHelper.FormatInsertSql(
                db.Config.Type,
                rank: currentRank,
                version: migration.Version,
                description: migration.Description,
                script: migration.ScriptFileName,
                checksum: checksum,
                installedBy: Environment.UserName ?? "runix",
                installedOn: now,
                executionTimeMs: sw.ElapsedMilliseconds,
                success: true
            );

            var insertResult = await db.ExecuteQueryAsync(insertSql, cancellationToken: ct);
            if (!insertResult.Success)
            {
                reports.Add(new MigrationExecutionReport(
                    Version: migration.Version,
                    ScriptFileName: migration.ScriptFileName,
                    Success: false,
                    ExecutionTimeMs: sw.ElapsedMilliseconds,
                    ErrorMessage: $"Migration DDL succeeded, but failed to record into {MigrationAuditTableHelper.TableName}: {insertResult.ErrorMessage}"
                ));

                return new MigrationResult(
                    Success: false,
                    AppliedCount: appliedCount,
                    Reports: reports,
                    ErrorMessage: $"Failed to update audit table for {migration.ScriptFileName}."
                );
            }

            appliedCount++;
            reports.Add(new MigrationExecutionReport(
                Version: migration.Version,
                ScriptFileName: migration.ScriptFileName,
                Success: true,
                ExecutionTimeMs: sw.ElapsedMilliseconds
            ));
        }

        return new MigrationResult(
            Success: true,
            AppliedCount: appliedCount,
            Reports: reports
        );
    }

    public async Task<string> ExportDiffAsMigrationAsync(
        string targetDirectory,
        string description,
        string ddlContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target migrations directory is required.", nameof(targetDirectory));
        }

        Directory.CreateDirectory(targetDirectory);

        // Find highest existing integer version
        int highestVersion = 0;
        var existingFiles = Directory.GetFiles(targetDirectory, "*.sql", SearchOption.TopDirectoryOnly);
        foreach (var file in existingFiles)
        {
            string name = Path.GetFileName(file);
            var match = MigrationFileRegex.Match(name);
            if (match.Success)
            {
                string ver = match.Groups["version"].Value;
                // Parse the first numeric component
                string majorStr = ver.Split('.')[0];
                if (int.TryParse(majorStr, out var v) && v > highestVersion)
                {
                    highestVersion = v;
                }
            }
        }

        int nextVersion = highestVersion + 1;
        string sanitizedDesc = string.IsNullOrWhiteSpace(description) ? "schema_sync" : description.Trim();
        sanitizedDesc = Regex.Replace(sanitizedDesc, @"[^a-zA-Z0-9_]+", "_").Trim('_');
        if (string.IsNullOrEmpty(sanitizedDesc))
        {
            sanitizedDesc = "schema_sync";
        }

        string fileName = $"V{nextVersion}__{sanitizedDesc}.sql";
        string fullPath = Path.Combine(targetDirectory, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine("-- RUNIX Database Studio — Migration Script");
        sb.AppendLine($"-- Version: {nextVersion}");
        sb.AppendLine($"-- Description: {sanitizedDesc}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine();
        sb.AppendLine(ddlContent.Trim());
        sb.AppendLine();

        await File.WriteAllTextAsync(fullPath, sb.ToString(), Encoding.UTF8, ct);
        return fullPath;
    }
}
