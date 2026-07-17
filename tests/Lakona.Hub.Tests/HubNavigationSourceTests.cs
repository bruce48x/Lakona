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
        Assert.Matches(
            "<StackPanel x:Name=\"SettingsSections\" Spacing=\"[0-9]+\" Margin=\"[0-9]+,[0-9]+,0,0\">",
            view);
        var developmentEnvironmentIndex = view.IndexOf("x:Name=\"DevelopmentEnvironmentCard\"", StringComparison.Ordinal);
        var applicationUpdatesIndex = view.IndexOf("x:Name=\"ApplicationUpdatesCard\"", StringComparison.Ordinal);
        var languageSettingsIndex = view.IndexOf("x:Name=\"LanguageSettingsCard\"", StringComparison.Ordinal);
        Assert.True(
            applicationUpdatesIndex >= 0 &&
            applicationUpdatesIndex < languageSettingsIndex &&
            languageSettingsIndex < developmentEnvironmentIndex);
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
        Assert.Contains("x:Name=\"RuntimeVersionText\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SdkInstallOverlay\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"InstallSdk_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("IHubSdkManager", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Click=\"CheckUpdate_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("IHubUpdateService", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HelpDialogOverlay\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CrashReportOverlay\"", view, StringComparison.Ordinal);
        Assert.Contains("HubCrashReporter.CreateIssueUrl", codeBehind, StringComparison.Ordinal);
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
    public void BorderlessWindowResizeGrips_AreHitTestableAndDoNotRaiseTheMinimumWidth()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Equal(8, Regex.Matches(
            view,
            "<Border Grid.ColumnSpan=\"2\"[^>]*Background=\"Transparent\"[^>]*PointerPressed=\"ResizeGrip_PointerPressed\"[^>]*/>").Count);
        Assert.DoesNotContain("<StackPanel Margin=\"64,52,36,48\" MinWidth=\"1060\">", view, StringComparison.Ordinal);
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
        Assert.Matches(
            "x:Name=\"EmptyProjectContent\" Spacing=\"[0-9]+\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"",
            view);
        Assert.Contains(
            "x:Name=\"EmptyProjectActions\" HorizontalAlignment=\"Center\"",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.WelcomeTitle", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Localization.Text.WelcomeDescription", view, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeTitle", localization, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeDescription", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedActions_UseResponsiveContainersInsteadOfFixedTextColumns()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("<WrapPanel HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">", view, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"ProjectExperience\" IsVisible=\"False\" HorizontalScrollBarVisibility=\"Disabled\"",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"196,24,196,16,220,*\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"*,18,132,12,174\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"48,20,*,24,190\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"48,20,*,24,320\"", view, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(view, "<Border Classes=\"dialog-surface\" MaxWidth=\"(?:480|540|560)\"").Count);
    }

    [Fact]
    public void ProjectList_SwitchesBetweenDenseTableAndCompactCards()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("SizeChanged=\"ProjectExperience_SizeChanged\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WideProjectList\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactProjectList\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WideProjectHeaders\"", view, StringComparison.Ordinal);
        Assert.Contains("WideProjectLayoutThreshold = 1180", codeBehind, StringComparison.Ordinal);
        Assert.Contains("WideProjectList.IsVisible = useWideLayout", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CompactProjectList.IsVisible = !useWideLayout", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectOverflowMenu_OffersActionsAndSafeListRemoval()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml.cs"));
        var localization = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "HubLocalization.cs"));

        Assert.Contains("Classes=\"project-more\"", view, StringComparison.Ordinal);
        Assert.Contains("<Button.ContextMenu>", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding OpenProjectFolderText}\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"OpenProjectFolder_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding RemoveFromListText}\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"RemoveProjectFromList_Click\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"{Binding ServerActionText}\" Click=\"OpenServer_Click\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"{Binding ClientActionText}\" Click=\"OpenClient_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("OpeningProjectFolder(project.Name)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Projects.Remove(project)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ProjectRemoved(project.Name)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("打开所在文件夹", localization, StringComparison.Ordinal);
        Assert.Contains("从列表中移除", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void HubVisualTokens_CentralizeMainWindowColorsAndTypography()
    {
        var repositoryRoot = FindRepositoryRoot();
        var application = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "App.axaml"));
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("x:Key=\"HubBrush.Window\"", application, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HubFont.Caption\"", application, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HubRadius.Control\"", application, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HubInset.Action\"", application, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"TextBlock.caption\">", view, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"TextBlock.page-title\">", view, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Border.settings-card\">", view, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"Border.dialog-surface\">", view, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(view, "#[0-9A-Fa-f]{6,8}"));
        Assert.Empty(Regex.Matches(view, "FontSize=\"[0-9]+\""));
    }

    [Fact]
    public void ExperienceStatus_UsesOneBindableSummaryInsteadOfPairedControlNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Equal(2, Regex.Matches(view, "StatusSummary\\.SdkStatusText").Count);
        Assert.Equal(2, Regex.Matches(view, "StatusSummary\\.EnvironmentSummaryText").Count);
        Assert.DoesNotContain("EmptySdkStatusText", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSdkStatusText", view, StringComparison.Ordinal);
        Assert.DoesNotContain("EmptyEnvironmentSummaryText", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectEnvironmentSummaryText", view, StringComparison.Ordinal);
        Assert.Contains("StatusSummary.SdkStatusText", codeBehind, StringComparison.Ordinal);
        Assert.Contains("StatusSummary.EnvironmentSummaryText", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Branding_UsesBundledImageInsteadOfPlatformDependentAsciiArt()
    {
        var repositoryRoot = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("Source=\"/Assets/lakona-hub-256.png\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"Lakona Hub\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily=\"monospace\"", view, StringComparison.Ordinal);
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
