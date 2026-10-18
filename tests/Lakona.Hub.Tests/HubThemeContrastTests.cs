using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Media;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubThemeContrastTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightTheme_EnabledTextRemainsReadableAcrossContentSurfaces()
    {
        var theme = ReadLightTheme();
        var surfaces = ContentSurfaces(theme).ToArray();

        foreach (var foreground in new[] { "TextPrimary", "TextSecondary", "TextMuted", "TextAccent", "TextError" })
        {
            foreach (var surface in surfaces)
            {
                AssertContrast(ReadBrush(theme, foreground), surface.Color, 4.5, foreground, surface.Name);
            }
        }
    }

    [Theory]
    [InlineData("Accent")]
    [InlineData("AccentHover")]
    [InlineData("AccentPressed")]
    public void LightTheme_AccentActionsKeepReadableLabelsInEveryEnabledState(string background)
    {
        var theme = ReadLightTheme();
        AssertContrast(ReadBrush(theme, "TextOnAccent"), ReadBrush(theme, background), 4.5, "TextOnAccent", background);
    }

    [Theory]
    [InlineData("BorderDefault")]
    [InlineData("BorderAccent")]
    [InlineData("BorderInteraction")]
    public void LightTheme_FieldAndFocusBoundariesRemainVisible(string border)
    {
        var theme = ReadLightTheme();
        foreach (var background in new[] { "Surface", "Window", "SurfaceRaised", "SurfaceHover" })
        {
            AssertContrast(ReadBrush(theme, border), ReadBrush(theme, background), 3, border, background);
        }
    }

    [Fact]
    public void DarkTheme_AccentTextAndInteractionBordersKeepTheBrandColor()
    {
        var theme = ReadTheme("Dark");
        var accent = ReadBrush(theme, "Accent");

        Assert.Equal(accent, ReadBrush(theme, "TextAccent"));
        Assert.Equal(accent, ReadBrush(theme, "BorderInteraction"));
    }

    [Fact]
    public void Window_ForegroundResourcesKeepIconsAndProgressVisibleOnLightSurfaces()
    {
        var theme = ReadLightTheme();
        var window = ReadXaml("MainWindow.axaml");
        var foregroundValues = window.Descendants()
            .SelectMany(element => element.Attributes().Where(attribute => attribute.Name.LocalName == "Foreground")
                .Select(attribute => attribute.Value))
            .Concat(window.Descendants().Where(element => element.Name.LocalName == "Setter"
                    && element.Attribute("Property")?.Value is "Foreground" or "TextElement.Foreground")
                .Select(element => element.Attribute("Value")?.Value ?? string.Empty));
        var brushKeys = foregroundValues
            .Select(value => Regex.Match(value, @"^\{(?:DynamicResource|StaticResource)\s+HubBrush\.(\w+)\s*\}$"))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(brushKeys);
        foreach (var foreground in brushKeys)
        {
            foreach (var background in new[] { "Surface", "Window" })
            {
                AssertContrast(ReadBrush(theme, foreground), ReadBrush(theme, background), 3, foreground, background);
            }
        }
    }

    private static IEnumerable<(string Name, Color Color)> ContentSurfaces(XElement theme)
    {
        foreach (var name in new[]
                 {
                     "Window", "Sidebar", "Surface", "SurfaceRaised", "SurfaceHover", "SurfaceListHover",
                     "SurfaceSelected", "SurfaceFeedback", "SurfaceAccentMuted"
                 })
        {
            yield return (name, ReadBrush(theme, name));
        }

        foreach (var name in new[] { "SidebarGradient", "DialogGradient" })
        {
            var gradient = FindResource(theme, name);
            foreach (var stop in gradient.Elements().Where(element => element.Name.LocalName == "GradientStop"))
            {
                yield return ($"{name} at {stop.Attribute("Offset")?.Value}", Color.Parse(stop.Attribute("Color")!.Value));
            }
        }
    }

    private static XElement ReadLightTheme() => ReadTheme("Light");

    private static XElement ReadTheme(string variant) => ReadXaml("App.axaml").Descendants()
        .Single(element => element.Name.LocalName == "ResourceDictionary"
            && element.Attribute(XamlNamespace + "Key")?.Value == variant);

    private static Color ReadBrush(XElement theme, string name) =>
        Color.Parse(FindResource(theme, name).Attribute("Color")!.Value);

    private static XElement FindResource(XElement theme, string name) =>
        theme.Elements().Single(element => element.Attribute(XamlNamespace + "Key")?.Value == $"HubBrush.{name}");

    private static void AssertContrast(Color foreground, Color background, double minimum, string foregroundName, string backgroundName)
    {
        Assert.Equal(byte.MaxValue, foreground.A);
        Assert.Equal(byte.MaxValue, background.A);
        var foregroundLuminance = Luminance(foreground);
        var backgroundLuminance = Luminance(background);
        var contrast = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05)
            / (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);

        Assert.True(contrast >= minimum,
            $"Light theme {foregroundName} on {backgroundName} has {contrast:F2}:1 contrast; expected at least {minimum}:1.");
    }

    private static double Luminance(Color color) =>
        0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);

    private static double LinearChannel(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static XDocument ReadXaml(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Lakona repository root.");
        return XDocument.Load(Path.Combine(root, "src", "Lakona.Hub", fileName));
    }
}
