using System.Collections.Generic;
using FluentAssertions;
using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Services;

public class SemVerComparerTests
{
    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("V2.3.4", "2.3.4")]
    [InlineData("1.1.0-beta.1", "1.1.0")]
    [InlineData("1.2.3+build99", "1.2.3")]
    [InlineData("  v1.4.0  ", "1.4.0")]
    public void NormalizeVersionString_ShouldStripPrefixAndMetadata(string input, string expected)
    {
        SemVerComparer.NormalizeVersionString(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("0.9.5", "1.0.0", false)]
    [InlineData("invalid", "1.0.0", false)]
    public void IsNewerVersion_ShouldCompareSemantically(string candidate, string current, bool expected)
    {
        SemVerComparer.IsNewerVersion(candidate, current).Should().Be(expected);
    }

    [Theory]
    [InlineData(UpdateTargetPlatform.MacOsArm64, "Runix-1.1.0-osx-arm64.dmg")]
    [InlineData(UpdateTargetPlatform.MacOsX64, "Runix-1.1.0-osx-x64.dmg")]
    [InlineData(UpdateTargetPlatform.WindowsX64, "Runix-Setup-1.1.0-win-x64.exe")]
    [InlineData(UpdateTargetPlatform.LinuxDebAmd64, "runix_1.1.0_amd64.deb")]
    [InlineData(UpdateTargetPlatform.LinuxRpmX64, "runix-1.1.0-1.x86_64.rpm")]
    public void ResolveRecommendedAsset_ShouldMatchPlatformInstallerFromReleaseAssets(
        UpdateTargetPlatform platform,
        string expectedAssetName)
    {
        var assets = new List<ReleaseAssetInfo>
        {
            new("Runix-1.1.0-osx-arm64.dmg", "https://example.com/Runix-1.1.0-osx-arm64.dmg", 1024),
            new("Runix-1.1.0-osx-x64.dmg", "https://example.com/Runix-1.1.0-osx-x64.dmg", 1024),
            new("Runix-Setup-1.1.0-win-x64.exe", "https://example.com/Runix-Setup-1.1.0-win-x64.exe", 2048),
            new("runix_1.1.0_amd64.deb", "https://example.com/runix_1.1.0_amd64.deb", 3072),
            new("runix-1.1.0-1.x86_64.rpm", "https://example.com/runix-1.1.0-1.x86_64.rpm", 3072)
        };

        var resolved = SemVerComparer.ResolveRecommendedAsset(assets, platform, "1.1.0");
        resolved.AssetName.Should().Be(expectedAssetName);
        resolved.DownloadUrl.Should().EndWith(expectedAssetName);
    }
}
