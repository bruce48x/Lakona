using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubNavigationSourceTests
{
    [Fact]
    public void SettingsPage_OwnsLanguageAndEnvironmentWithoutSeparateEnvironmentNavigation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("x:Name=\"SettingsExperience\"", view, StringComparison.Ordinal);
        Assert.Contains("Localization.LanguageOptions", view, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ApplicationTools}\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"BrowseApplicationPath_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("SuggestedStartLocation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Environment.SpecialFolder.UserProfile", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplicationPathStore", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateButton\"", view, StringComparison.Ordinal);
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
        Assert.DoesNotContain("<Setter Property=\"HorizontalContentAlignment\" Value=\"Left\" />", view, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"0\" Spacing=\"2\" HorizontalAlignment=\"Center\">", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"Lakona Hub\" FontSize=\"22\" FontWeight=\"SemiBold\" HorizontalAlignment=\"Center\" TextAlignment=\"Center\"", view, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(view, "Text=\"{Binding Localization.Text.CreateProject}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(2, CountOccurrences(view, "Text=\"{Binding Localization.Text.ImportExistingProject}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(1, CountOccurrences(view, "Text=\"{Binding Localization.Text.RefreshDetection}\"", "VerticalAlignment=\"Center\""));
        Assert.Equal(1, CountOccurrences(view, "x:Name=\"UpdateButtonText\"", "VerticalAlignment=\"Center\""));
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
