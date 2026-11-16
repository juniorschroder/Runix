using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Runix.Core.Tests.Accessibility;

public class AccessibilityAttributesTests
{
    private static DirectoryInfo GetSolutionRoot()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Runix.sln")))
        {
            currentDir = currentDir.Parent;
        }

        currentDir.Should().NotBeNull("Runix.sln root should be discoverable from test execution path");
        return currentDir!;
    }

    [Fact]
    public void AccessibilityKeys_ShouldExistInBothCatalogs()
    {
        var root = GetSolutionRoot();
        var ptBrPath = Path.Combine(root.FullName, "src", "Runix.Desktop", "Locale", "Strings.pt-BR.axaml");
        var enUsPath = Path.Combine(root.FullName, "src", "Runix.Desktop", "Locale", "Strings.en-US.axaml");

        var ptText = File.ReadAllText(ptBrPath);
        var enText = File.ReadAllText(enUsPath);

        var expectedA11yKeys = new[]
        {
            "A11y.MinimizeWindow",
            "A11y.MaximizeWindow",
            "A11y.CloseWindow",
            "A11y.AboutApp",
            "A11y.LanguageSwitch",
            "A11y.SearchConnections",
            "A11y.SavedConnectionsList",
            "A11y.NewConnection",
            "A11y.ConnectionName",
            "A11y.Connect",
            "A11y.SaveConnection",
            "A11y.DeleteConnection",
            "A11y.TestConnection",
            "A11y.NewQueryTab",
            "A11y.FilterTables",
            "A11y.ObjectTree",
            "A11y.WorkspaceTabs",
            "A11y.SqlEditor",
            "A11y.ExecuteSql",
            "A11y.QueryResultsGrid",
            "A11y.FilterTableData",
            "A11y.PrevPage",
            "A11y.NextPage",
            "A11y.AddRow",
            "A11y.DeleteRow",
            "A11y.SaveStagedChanges",
            "A11y.DiscardStagedChanges",
            "A11y.RefreshData",
            "A11y.CloseModal",
            "A11y.ErdCanvas",
            "A11y.ChartCanvas"
        };

        foreach (var key in expectedA11yKeys)
        {
            ptText.Should().Contain($"x:Key=\"{key}\"", $"pt-BR catalog must define accessibility key {key}");
            enText.Should().Contain($"x:Key=\"{key}\"", $"en-US catalog must define accessibility key {key}");
        }
    }

    [Theory]
    [InlineData("MainWindow.axaml", "AutomationProperties.Name")]
    [InlineData("Views/ConnectionView.axaml", "AutomationProperties.Name")]
    [InlineData("Views/ConnectionView.axaml", "AutomationProperties.LiveSetting")]
    [InlineData("Views/WorkspaceView.axaml", "AutomationProperties.Name")]
    [InlineData("Views/QueryTabControl.axaml", "AutomationProperties.Name")]
    [InlineData("Views/QueryTabControl.axaml", "AutomationProperties.LiveSetting")]
    [InlineData("Views/TableDataTabControl.axaml", "AutomationProperties.Name")]
    [InlineData("Views/TableDataTabControl.axaml", "AutomationProperties.LiveSetting")]
    [InlineData("Views/ErdTabControl.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Chart/SkiaChartControl.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Dialogs/AboutDialog.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Dialogs/UpdateAvailableDialog.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Dialogs/ExportDialog.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Dialogs/CommandPaletteDialog.axaml", "AutomationProperties.Name")]
    [InlineData("Views/Dialogs/BackupRestoreDialog.axaml", "AutomationProperties.Name")]
    public void Views_ShouldContainAccessibilityProperties(string relativePath, string expectedProperty)
    {
        var root = GetSolutionRoot();
        var fullPath = Path.Combine(root.FullName, "src", "Runix.Desktop", relativePath);

        File.Exists(fullPath).Should().BeTrue($"File {relativePath} must exist");
        var content = File.ReadAllText(fullPath);

        content.Should().Contain(expectedProperty, $"File {relativePath} must include accessibility property '{expectedProperty}' for screen reader support");
    }
}
