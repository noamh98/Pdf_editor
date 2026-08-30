using Avalonia;
using Avalonia.Styling;
using PdfEditor.Core.Settings;

namespace PdfEditor.App.Services;

/// <summary>Applies the user's theme choice, or follows Windows when they chose to.</summary>
public static class ThemeApplier
{
    public static void Apply(Application application, ThemePreference preference)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.RequestedThemeVariant = ToVariant(preference);
    }

    public static ThemeVariant ToVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        // Default asks the platform, which on Windows means the app theme in Settings.
        _ => ThemeVariant.Default
    };
}
