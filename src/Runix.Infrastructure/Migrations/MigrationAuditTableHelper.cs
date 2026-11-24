using System;
using System.Collections.Generic;
using Runix.Core.Models;
using Runix.Core.Models.Migrations;

namespace Runix.Infrastructure.Migrations;

public static class MigrationAuditTableHelper
{
    public const string TableName = "__runix_migrations";

    public static string GetCreateTableDdl(DatabaseType type)
    {
        return type switch
        {
            DatabaseType.SQLite => @"
CREATE TABLE IF NOT EXISTS __runix_migrations (
    installed_rank INTEGER PRIMARY KEY,
    version TEXT NOT NULL,
    description TEXT NOT NULL,
    type TEXT NOT NULL,
    script TEXT NOT NULL,
    checksum TEXT NOT NULL,
    installed_by TEXT,
    installed_on TEXT NOT NULL,
    execution_time_ms INTEGER NOT NULL,
    success INTEGER NOT NULL
);",

            DatabaseType.PostgreSQL => @"
CREATE TABLE IF NOT EXISTS __runix_migrations (
    installed_rank INTEGER PRIMARY KEY,
    version VARCHAR(50) NOT NULL,
    description VARCHAR(200) NOT NULL,
    type VARCHAR(20) NOT NULL,
    script VARCHAR(1000) NOT NULL,
    checksum VARCHAR(64) NOT NULL,
    installed_by VARCHAR(100),
    installed_on TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    execution_time_ms INTEGER NOT NULL,
    success BOOLEAN NOT NULL
);",

            DatabaseType.MySQL or DatabaseType.MariaDB => @"
CREATE TABLE IF NOT EXISTS `__runix_migrations` (
    `installed_rank` INT NOT NULL,
    `version` VARCHAR(50) NOT NULL,
    `description` VARCHAR(200) NOT NULL,
    `type` VARCHAR(20) NOT NULL,
    `script` VARCHAR(1000) NOT NULL,
    `checksum` VARCHAR(64) NOT NULL,
    `installed_by` VARCHAR(100) NULL,
    `installed_on` DATETIME NOT NULL,
    `execution_time_ms` INT NOT NULL,
    `success` TINYINT(1) NOT NULL,
    PRIMARY KEY (`installed_rank`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            DatabaseType.SqlServer => @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__runix_migrations')
BEGIN
    CREATE TABLE [dbo].[__runix_migrations] (
        [installed_rank] INT NOT NULL,
        [version] NVARCHAR(50) NOT NULL,
        [description] NVARCHAR(200) NOT NULL,
        [type] NVARCHAR(20) NOT NULL,
        [script] NVARCHAR(1000) NOT NULL,
        [checksum] NVARCHAR(64) NOT NULL,
        [installed_by] NVARCHAR(100) NULL,
        [installed_on] DATETIME2 NOT NULL,
        [execution_time_ms] INT NOT NULL,
        [success] BIT NOT NULL,
        CONSTRAINT [PK___runix_migrations] PRIMARY KEY ([installed_rank])
    );
END;",

            _ => throw new NotSupportedException($"Database engine {type} is not supported for migrations.")
        };
    }

    public static string GetSelectHistoryQuery(DatabaseType type)
    {
        string table = type switch
        {
            DatabaseType.MySQL or DatabaseType.MariaDB => "`__runix_migrations`",
            DatabaseType.SqlServer => "[dbo].[__runix_migrations]",
            _ => "__runix_migrations"
        };

        return $"SELECT installed_rank, version, description, type, script, checksum, installed_by, installed_on, execution_time_ms, success FROM {table} ORDER BY installed_rank ASC;";
    }

    public static string FormatInsertSql(DatabaseType type, int rank, string version, string description, string script, string checksum, string? installedBy, DateTime installedOn, long executionTimeMs, bool success)
    {
        string table = type switch
        {
            DatabaseType.MySQL or DatabaseType.MariaDB => "`__runix_migrations`",
            DatabaseType.SqlServer => "[dbo].[__runix_migrations]",
            _ => "__runix_migrations"
        };

        string safeVersion = version.Replace("'", "''");
        string safeDesc = description.Replace("'", "''");
        string safeScript = script.Replace("'", "''");
        string safeChecksum = checksum.Replace("'", "''");
        string safeUser = (installedBy ?? Environment.UserName ?? "runix").Replace("'", "''");
        string dateStr = installedOn.ToString("yyyy-MM-dd HH:mm:ss");
        string successVal = type switch
        {
            DatabaseType.PostgreSQL => success ? "TRUE" : "FALSE",
            _ => success ? "1" : "0"
        };

        return $@"INSERT INTO {table} (installed_rank, version, description, type, script, checksum, installed_by, installed_on, execution_time_ms, success)
VALUES ({rank}, '{safeVersion}', '{safeDesc}', 'SQL', '{safeScript}', '{safeChecksum}', '{safeUser}', '{dateStr}', {executionTimeMs}, {successVal});";
    }
}
