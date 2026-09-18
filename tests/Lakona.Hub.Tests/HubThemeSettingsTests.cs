using Avalonia.Styling;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubThemeSettingsTests
{
    [Fact]
    public void Selection_AppliesThemeAndRaisesPersistenceSignal()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var applied = new List<ThemeVariant>();
        using var settings = new HubThemeSettings(localization, HubThemePreference.System, applied.Add);
        var changes = 0;
        settings.PreferenceChanged += (_, _) => changes++;

        settings.SelectedOption = settings.Options.Single(option => option.Preference == HubThemePreference.Light);

        Assert.Equal(HubThemePreference.Light, settings.Preference);
        Assert.Equal([ThemeVariant.Default, ThemeVariant.Light], applied);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void LanguageChange_RebuildsLabelsWithoutChangingTheme()
    {
        var localization = new HubLocalization(HubLanguage.SimplifiedChinese);
        var applied = new List<ThemeVariant>();
        using var settings = new HubThemeSettings(localization, HubThemePreference.Dark, applied.Add);

        localization.SetLanguage(HubLanguage.English);

        Assert.Equal(["Follow system", "Dark", "Light"], settings.Options.Select(option => option.DisplayName));
        Assert.Equal(HubThemePreference.Dark, settings.Preference);
        Assert.Equal([ThemeVariant.Dark], applied);
    }
}
