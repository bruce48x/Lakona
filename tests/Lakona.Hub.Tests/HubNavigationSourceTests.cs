using System.Text.RegularExpressions;
using Avalonia.Input;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubNavigationSourceTests
{
    [Fact]
    public void MainWindow_UsesOnlyRuntimeRecognizedCursorNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var cursorNames = Regex.Matches(view, "Cursor=\"(?<name>[^\"]+)\"")
            .Select(match => match.Groups["name"].Value)
            .ToArray();

        Assert.NotEmpty(cursorNames);
        Assert.All(cursorNames, name => Assert.True(
            Enum.TryParse<StandardCursorType>(name, out _),
            $"Unrecognized Avalonia cursor name: {name}"));
    }

    [Fact]
    public void SettingsPage_OwnsLanguageAndEnvironmentWithoutSeparateEnvironmentNavigation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("x:Name=\"SettingsExperience\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "<StackPanel x:Name=\"SettingsSections\" Spacing=\"24\" Margin=\"0,42,0,0\">",
            view,
            StringComparison.Ordinal);
        var developmentEnvironmentIndex = view.IndexOf("x:Name=\"DevelopmentEnvironmentCard\"", StringComparison.Ordinal);
        var applicationUpdatesIndex = view.IndexOf("x:Name=\"ApplicationUpdatesCard\"", StringComparison.Ordinal);
        var languageSettingsIndex = view.IndexOf("x:Name=\"LanguageSettingsCard\"", StringComparison.Ordinal);
        Assert.True(
            developmentEnvironmentIndex >= 0 &&
            developmentEnvironmentIndex < applicationUpdatesIndex &&
            applicationUpdatesIndex < languageSettingsIndex);
        Assert.Contains("Localization.LanguageOptions", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.LanguageDescription", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.DisplayLanguageHint", view, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ApplicationTools}\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"ApplicationToolAction_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"AddApplicationTool_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("Localization.Text.AddApplication", view, StringComparison.Ordinal);
        Assert.Contains("SuggestedStartLocation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Environment.SpecialFolder.UserProfile", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ManualApplicationStore", codeBehind, StringComparison.Ordinal);
        Assert.Contains("LoadStartupSettings()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RestoreProjects(settings.Projects)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TrySaveUserSettings()", codeBehind, StringComparison.Ordinal);
        Assert.True(
            codeBehind.IndexOf("ApplyApplications();", StringComparison.Ordinal) <
            codeBehind.IndexOf("RestoreProjects(settings.Projects);", StringComparison.Ordinal));
        Assert.Contains("x:Name=\"UpdateButton\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateDownloadProgress\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateDownloadProgressText\"", view, StringComparison.Ordinal);
        Assert.Contains("HubUpdateProgress", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CanResize=\"True\"", view, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1000\"", view, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"800\"", view, StringComparison.Ordinal);
        Assert.Contains("PointerPressed=\"ResizeGrip_PointerPressed\"", view, StringComparison.Ordinal);
        Assert.Contains("BeginResizeDrag(edge, e)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding BundledDotNetSdkLabel}\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"CheckUpdate_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("IHubUpdateService", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HelpDialogOverlay\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenHelpIssues_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("https://github.com/bruce48x/Lakona/issues", codeBehind, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("HelpComingSoon", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.SettingsDescription", view, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"Environment_Click\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("void Environment_Click", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAndProjectActions_CenterTheirContent()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("<Setter Property=\"HorizontalContentAlignment\" Value=\"Center\" />", view, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"VerticalContentAlignment\" Value=\"Center\" />", view, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Button > StackPanel\">", view, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Button TextBlock\">", view, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"HorizontalContentAlignment\" Value=\"Left\" />", view, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(view, "Text=\"{Binding Localization.Text.CreateProject}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(2, CountOccurrences(view, "Text=\"{Binding Localization.Text.ImportExistingProject}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(1, CountOccurrences(view, "Text=\"{Binding Localization.Text.RefreshDetection}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(1, CountOccurrences(view, "x:Name=\"UpdateButtonText\"", "VerticalAlignment=\"Center\""));
    }

    [Fact]
    public void EmptyProjectExperience_CentersActionsWithoutWelcomeCopy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var localization = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "HubLocalization.cs"));

        Assert.Contains("x:Name=\"EmptyExperience\"", view, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"EmptyProjectContent\" Spacing=\"38\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"EmptyProjectActions\" Orientation=\"Horizontal\" Spacing=\"18\" HorizontalAlignment=\"Center\"",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.WelcomeTitle", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.WelcomeDescription", view, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeTitle", localization, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeDescription", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void Branding_UsesSharedCharacterArtWithoutRuntimeImageDecoding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains(@"/\_/\&#10; ( oᴥo )&#10;  U___U", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"Lakona Hub\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"AnimalCat\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("lakona-cat.png", view, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string view, string label, string alignment)
    {
        return view.Split('\n').Count(line =>
            line.Contains(label, StringComparison.Ordinal) &&
            line.Contains(alignment, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
