using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Styling;

namespace Lakona.Hub;

public enum HubThemePreference
{
    System,
    Dark,
    Light
}

public sealed record HubThemeOption(HubThemePreference Preference, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class HubThemeSettings : INotifyPropertyChanged, IDisposable
{
    private readonly HubLocalization localization;
    private readonly Action<ThemeVariant> applyTheme;
    private HubThemeOption selectedOption;

    public HubThemeSettings(HubLocalization localization, HubThemePreference preference)
        : this(localization, preference, ApplyToCurrentApplication)
    {
    }

    internal HubThemeSettings(
        HubLocalization localization,
        HubThemePreference preference,
        Action<ThemeVariant> applyTheme)
    {
        this.localization = localization;
        this.applyTheme = applyTheme;
        Options = CreateOptions();
        selectedOption = OptionFor(preference);
        localization.PropertyChanged += Localization_PropertyChanged;
        applyTheme(ToThemeVariant(preference));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PreferenceChanged;

    public IReadOnlyList<HubThemeOption> Options { get; private set; }

    public HubThemeOption SelectedOption
    {
        get => selectedOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (selectedOption.Preference == value.Preference)
            {
                return;
            }

            selectedOption = OptionFor(value.Preference);
            applyTheme(ToThemeVariant(selectedOption.Preference));
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preference));
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public HubThemePreference Preference => selectedOption.Preference;

    internal static ThemeVariant ToThemeVariant(HubThemePreference preference) => preference switch
    {
        HubThemePreference.System => ThemeVariant.Default,
        HubThemePreference.Dark => ThemeVariant.Dark,
        HubThemePreference.Light => ThemeVariant.Light,
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, null)
    };

    public void Dispose() => localization.PropertyChanged -= Localization_PropertyChanged;

    private static void ApplyToCurrentApplication(ThemeVariant themeVariant)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = themeVariant;
        }
    }

    private IReadOnlyList<HubThemeOption> CreateOptions() =>
    [
        new(HubThemePreference.System, localization.Text.FollowSystemTheme),
        new(HubThemePreference.Dark, localization.Text.DarkTheme),
        new(HubThemePreference.Light, localization.Text.LightTheme)
    ];

    private HubThemeOption OptionFor(HubThemePreference preference) =>
        Options.First(option => option.Preference == preference);

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HubLocalization.Text))
        {
            return;
        }

        var preference = Preference;
        Options = CreateOptions();
        selectedOption = OptionFor(preference);
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(SelectedOption));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
