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
        AppConstants.AppVersion.Should().Be("1.0.0");
        AppConstants.AppAuthor.Should().Be("José Carlos Schröder Júnior");
        AppConstants.AppCopyright.Should().Contain("José Carlos Schröder Júnior");
        AppConstants.AppLicense.Should().Be("MIT License");
        AppConstants.AppRepositoryUrl.Should().Be("https://github.com/juniorschroder/Runix");
        AppConstants.AppAuthorLinkedInUrl.Should().Be("https://www.linkedin.com/in/juniorschroder/");
    }
}
