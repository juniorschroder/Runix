using Runix.Core.Models;
using Runix.Core.Models.Diff;
using Xunit;

namespace Runix.Core.Tests.Models;

public class CompareEndpointTests
{
    [Fact]
    public void ActiveConnection_ClassifiedCorrectly()
    {
        var endpoint = new CompareEndpoint
        {
            Type = CompareEndpointType.ActiveConnection,
            DisplayName = "[Ativa] Conexão Atual",
            Dialect = DatabaseType.PostgreSQL
        };

        Assert.True(endpoint.IsLiveDatabase);
        Assert.False(endpoint.IsOfflineSnapshot);
        Assert.Equal("[Ativa] Conexão Atual", endpoint.ToString());
    }

    [Fact]
    public void SavedConnection_ClassifiedCorrectly()
    {
        var endpoint = new CompareEndpoint
        {
            Type = CompareEndpointType.SavedConnection,
            DisplayName = "Produção PG",
            SavedConnection = new ConnectionConfig { Name = "Produção PG", Type = DatabaseType.PostgreSQL }
        };

        Assert.True(endpoint.IsLiveDatabase);
        Assert.False(endpoint.IsOfflineSnapshot);
    }

    [Fact]
    public void LocalCatalogSnapshot_ClassifiedCorrectly()
    {
        var item = new SnapshotCatalogItem
        {
            FilePath = "/home/user/.runix/snapshots/backup.rschema",
            DatabaseName = "ecommerce",
            EngineType = "PostgreSQL"
        };

        var endpoint = new CompareEndpoint
        {
            Type = CompareEndpointType.LocalCatalogSnapshot,
            DisplayName = "[Snapshot] ecommerce",
            CatalogItem = item,
            FilePath = "/home/user/.runix/snapshots/backup.rschema"
        };

        Assert.False(endpoint.IsLiveDatabase);
        Assert.True(endpoint.IsOfflineSnapshot);
    }

    [Fact]
    public void SnapshotFile_ClassifiedCorrectly()
    {
        var endpoint = new CompareEndpoint
        {
            Type = CompareEndpointType.SnapshotFile,
            DisplayName = "custom_export.rschema",
            FilePath = "/tmp/custom_export.rschema",
            Snapshot = new SchemaSnapshotModel { DatabaseName = "test_db" }
        };

        Assert.False(endpoint.IsLiveDatabase);
        Assert.True(endpoint.IsOfflineSnapshot);
        Assert.NotNull(endpoint.Snapshot);
    }
}
