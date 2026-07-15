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
        Assert.Contains("SettingsEnvironmentSummaryText", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UpdateButton\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"CheckUpdate_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("IHubUpdateService", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"Environment_Click\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("void Environment_Click", codeBehind, StringComparison.Ordinal);
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
