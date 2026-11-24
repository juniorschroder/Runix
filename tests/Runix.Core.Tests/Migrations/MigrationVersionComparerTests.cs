using System.Collections.Generic;
using Runix.Core.Models.Migrations;
using Xunit;

namespace Runix.Core.Tests.Migrations;

public class MigrationVersionComparerTests
{
    private readonly MigrationVersionComparer _comparer = MigrationVersionComparer.Instance;

    [Theory]
    [InlineData("1", "2", -1)]
    [InlineData("2", "1", 1)]
    [InlineData("1", "1", 0)]
    [InlineData("2", "10", -1)]
    [InlineData("10", "2", 1)]
    [InlineData("V1", "V2", -1)]
    [InlineData("V2", "V10", -1)]
    [InlineData("v10", "v2", 1)]
    [InlineData("1.0", "1.1", -1)]
    [InlineData("1.1", "1.0", 1)]
    [InlineData("1.9", "1.10", -1)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("2.0.0", "2.0.1", -1)]
    [InlineData("2.0.1", "2.0.0", 1)]
    [InlineData("2.0", "2.0.0", 0)]
    public void MigrationVersionComparer_SortsCorrectly(string v1, string v2, int expectedSign)
    {
        int result = _comparer.Compare(v1, v2);
        if (expectedSign < 0) Assert.True(result < 0);
        else if (expectedSign > 0) Assert.True(result > 0);
        else Assert.Equal(0, result);
    }

    [Fact]
    public void MigrationVersionComparer_SortsListOfVersionsNaturally()
    {
        var versions = new List<string> { "10", "2", "1", "1.1", "2.1", "20", "1.0.1" };
        versions.Sort(_comparer);

        var expected = new List<string> { "1", "1.0.1", "1.1", "2", "2.1", "10", "20" };
        Assert.Equal(expected, versions);
    }
}
