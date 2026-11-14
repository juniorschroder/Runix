using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Runix.Core.Tests.Localization;

public class LocalizationCatalogTests
{
    private static readonly XNamespace XamlNs = "https://github.com/avaloniaui";
    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LocalizationCatalogs_ShouldExistAndHave100PercentKeyParity()
    {
        // Arrange - locate files
        var basePath = AppContext.BaseDirectory;
        // Search upwards to find solution root
        var currentDir = new DirectoryInfo(basePath);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Runix.sln")))
        {
            currentDir = currentDir.Parent;
        }

        currentDir.Should().NotBeNull("Runix.sln root should be discoverable from test execution path");

        var ptBrPath = Path.Combine(currentDir!.FullName, "src", "Runix.Desktop", "Locale", "Strings.pt-BR.axaml");
        var enUsPath = Path.Combine(currentDir.FullName, "src", "Runix.Desktop", "Locale", "Strings.en-US.axaml");

        File.Exists(ptBrPath).Should().BeTrue($"File should exist at {ptBrPath}");
        File.Exists(enUsPath).Should().BeTrue($"File should exist at {enUsPath}");

        // Act - Parse XML and extract keys
        var ptKeys = LoadKeysFromAxaml(ptBrPath);
        var enKeys = LoadKeysFromAxaml(enUsPath);

        // Assert
        ptKeys.Count.Should().BeGreaterThan(40, "pt-BR catalog should contain all expected application keys");
        enKeys.Count.Should().BeGreaterThan(40, "en-US catalog should contain all expected application keys");

        var missingInEn = ptKeys.Keys.Except(enKeys.Keys).ToList();
        var missingInPt = enKeys.Keys.Except(ptKeys.Keys).ToList();

        missingInEn.Should().BeEmpty("all keys present in pt-BR must be present in en-US");
        missingInPt.Should().BeEmpty("all keys present in en-US must be present in pt-BR");

        // Verify no empty or whitespace-only values
        foreach (var (key, value) in ptKeys)
        {
            value.Should().NotBeNullOrWhiteSpace($"pt-BR value for key '{key}' should not be blank");
        }

        foreach (var (key, value) in enKeys)
        {
            value.Should().NotBeNullOrWhiteSpace($"en-US value for key '{key}' should not be blank");
        }
    }

    private static Dictionary<string, string> LoadKeysFromAxaml(string filePath)
    {
        var doc = XDocument.Load(filePath);
        var dict = new Dictionary<string, string>();

        foreach (var elem in doc.Descendants())
        {
            var keyAttr = elem.Attribute(XNs + "Key");
            if (keyAttr != null && !string.IsNullOrWhiteSpace(keyAttr.Value))
            {
                dict[keyAttr.Value] = elem.Value;
            }
        }

        return dict;
    }
}
