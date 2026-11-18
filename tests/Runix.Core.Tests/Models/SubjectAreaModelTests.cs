using Runix.Core.Models;
using Xunit;

namespace Runix.Core.Tests.Models;

public class SubjectAreaModelTests
{
    [Fact]
    public void DefaultInstantiation_HasExpectedDefaults()
    {
        var area = new SubjectAreaModel();

        Assert.False(string.IsNullOrWhiteSpace(area.Id));
        Assert.Equal("Nova Área", area.Title);
        Assert.Equal(SubjectAreaPalette.DefaultColor, area.ColorHex);
        Assert.Equal(420.0, area.Width);
        Assert.Equal(320.0, area.Height);
        Assert.Empty(area.TableNames);
        Assert.Equal(0, area.TableCount);
    }

    [Fact]
    public void AddAndRemoveTable_MaintainsSetIntegrity()
    {
        var area = new SubjectAreaModel();

        area.AddTable("users");
        area.AddTable("USERS"); // Case insensitive
        area.AddTable("orders");

        Assert.Equal(2, area.TableCount);
        Assert.Contains("users", area.TableNames);
        Assert.Contains("orders", area.TableNames);

        area.RemoveTable("users");
        Assert.Single(area.TableNames);
        Assert.DoesNotContain("users", area.TableNames);
    }

    [Fact]
    public void ContainsTableCenter_AccuratelyEvaluatesContainment()
    {
        var area = new SubjectAreaModel
        {
            X = 100,
            Y = 100,
            Width = 400,
            Height = 300
        };

        // Node inside area: Center is (150 + 125 = 275, 150 + 50 = 200) -> inside [100, 500] x [100, 400]
        var insideNode = new ErdTableNode
        {
            Table = new TableInfo { Name = "inside_table" },
            X = 150,
            Y = 150,
            Width = 200
        };
        Assert.True(area.ContainsTableCenter(insideNode));

        // Node outside area: Center is (600 + 125 = 725, 600 + 50 = 650) -> outside
        var outsideNode = new ErdTableNode
        {
            Table = new TableInfo { Name = "outside_table" },
            X = 600,
            Y = 600,
            Width = 200
        };
        Assert.False(area.ContainsTableCenter(outsideNode));
    }

    [Fact]
    public void SubjectAreaPalette_ValidatesAndNamesAllEightColors()
    {
        Assert.Equal(8, SubjectAreaPalette.AllColors.Count);

        foreach (var color in SubjectAreaPalette.AllColors)
        {
            Assert.True(SubjectAreaPalette.IsValidColor(color));
            Assert.NotEqual("Personalizado", SubjectAreaPalette.GetColorName(color));
        }

        Assert.False(SubjectAreaPalette.IsValidColor("#ffffff"));
        Assert.False(SubjectAreaPalette.IsValidColor(null));
        Assert.Equal("Personalizado", SubjectAreaPalette.GetColorName("#123456"));
    }
}
