using Microsoft.UI.Xaml;
using Windows.Storage;

namespace NexCode.Gui.Services;

public enum NexCodeTheme
{
    Dark,
    Light,
    HighContrast
}

/// <summary>
/// Manages the runtime theme selection for the GUI process. Persists the user's
/// last choice via <see cref="ApplicationData.LocalSettings"/> when running in a
/// packaged context; otherwise the choice survives only for the lifetime of the
/// process.
/// </summary>
public sealed class ThemeService
{
    private const string ThemeSettingKey = "NexCode.Theme";

    private FrameworkElement? _themeRoot;
    private NexCodeTheme _current = NexCodeTheme.Dark;

    public NexCodeTheme Current => _current;

    /// <summary>
    /// Attaches the theme root element used to control <see cref="FrameworkElement.RequestedTheme"/>.
    /// </summary>
    public void AttachRoot(FrameworkElement root)
    {
        _themeRoot = root ?? throw new ArgumentNullException(nameof(root));
        ApplyTheme(LoadPersistedTheme());
    }

    /// <summary>Apply a theme using a NexCode-typed value.</summary>
    public void ApplyTheme(NexCodeTheme theme)
    {
        _current = theme;
        if (_themeRoot is not null)
        {
            _themeRoot.RequestedTheme = MapTheme(theme);
        }
        PersistTheme(theme);
    }

    /// <summary>Apply a theme using the WinUI <see cref="ElementTheme"/> enum.</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        ApplyTheme(MapTheme(theme));
    }

    public NexCodeTheme GetCurrent() => _current;

    private static ElementTheme MapTheme(NexCodeTheme theme) => theme switch
    {
        NexCodeTheme.Light => ElementTheme.Light,
        NexCodeTheme.Dark => ElementTheme.Dark,
        // HighContrast is driven by Windows but we still apply Dark as a sensible default
        // for the WinUI tree because Windows itself overlays high-contrast theme dictionaries.
        _ => ElementTheme.Default
    };

    private static NexCodeTheme MapTheme(ElementTheme theme) => theme switch
    {
        ElementTheme.Light => NexCodeTheme.Light,
        ElementTheme.Dark => NexCodeTheme.Dark,
        _ => NexCodeTheme.HighContrast
    };

    private static NexCodeTheme LoadPersistedTheme()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(ThemeSettingKey, out var raw) && raw is string s)
            {
                if (Enum.TryParse<NexCodeTheme>(s, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // unpackaged or unavailable — fall through
        }

        return NexCodeTheme.Dark;
    }

    private static void PersistTheme(NexCodeTheme theme)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[ThemeSettingKey] = theme.ToString();
        }
        catch
        {
            // unpackaged — ignore
        }
    }
}
