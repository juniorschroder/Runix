using System;
using System.IO;
using FluentAssertions;
using Runix.Core.Constants;
using Xunit;

namespace Runix.Core.Tests.Constants;

public class AppConstantsTests
{
    [Fact]
    public void AppConstants_WindowDimensions_MatchDesignSystemDefaults()
    {
        AppConstants.WindowDefaultWidth.Should().Be(1200);
        AppConstants.WindowDefaultHeight.Should().Be(800);
        AppConstants.WindowMinWidth.Should().Be(800);
        AppConstants.WindowMinHeight.Should().Be(600);
    }

    [Fact]
    public void AppConstants_QueryAndSidebarLimits_AreSane()
    {
        AppConstants.PrimarySidebarMinWidth.Should().Be(150);
        AppConstants.MaxResults.Should().Be(50000);
        AppConstants.MainContentMinWidth.Should().Be(200);
    }

    [Fact]
    public void AppConstants_VersionAndCreatorMetadata_ShouldBeCorrect()
    {
        AppConstants.AppName.Should().Be("RUNIX Database Studio");
        AppConstants.AppVersion.Should().Be("1.6.0");
        AppConstants.AppAuthor.Should().Be("José Carlos Schröder Júnior");
        AppConstants.AppCopyright.Should().Contain("José Carlos Schröder Júnior");
        AppConstants.AppLicense.Should().Be("GPL-3.0 License");
        AppConstants.AppRepositoryUrl.Should().Be("https://github.com/juniorschroder/Runix");
        AppConstants.AppAuthorLinkedInUrl.Should().Be("https://www.linkedin.com/in/juniorschroder/");
    }

    [Fact]
    public void AppConstants_DefaultSnapshotsDirectory_ShouldPointToUserRunixSnapshots()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".runix", "snapshots");
        AppConstants.DefaultSnapshotsDirectory.Should().Be(expected);
    }
}
