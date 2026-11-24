using System;
using Runix.Core.Interfaces;
using Runix.Core.Models;
using Runix.Infrastructure.Data;

namespace Runix.Infrastructure.Factories;

public static class DatabaseServiceFactory
{
    public static IDatabaseService Create(ConnectionConfig config, ISshTunnelService? sshTunnelService = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.Type switch
        {
            DatabaseType.SQLite => new SqliteDatabaseService(config),
            DatabaseType.PostgreSQL => new PostgresDatabaseService(config, sshTunnelService),
            DatabaseType.MySQL or DatabaseType.MariaDB => new MySqlDatabaseService(config, sshTunnelService),
            DatabaseType.SqlServer => new SqlServerDatabaseService(config, sshTunnelService),
            _ => throw new NotSupportedException($"Database engine '{config.Type}' is not supported.")
        };
    }
}
