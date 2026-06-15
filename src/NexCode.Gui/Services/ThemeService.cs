using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;

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

    // Held for the lifetime of the service so the ColorValuesChanged subscription stays alive.
    private UISettings? _uiSettings;
    private DispatcherQueue? _dispatcherQueue;

    public NexCodeTheme Current => _current;

    /// <summary>
    /// Attaches the theme root element used to control <see cref="FrameworkElement.RequestedTheme"/>.
    /// </summary>
    public void AttachRoot(FrameworkElement root)
    {
        _themeRoot = root ?? throw new ArgumentNullException(nameof(root));
        ApplyTheme(LoadPersistedTheme());
        InitializeSystemAccent();
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

        // Re-evaluate the accent override after a theme switch so the High Contrast
        // dictionary's system-color accents are honored when appropriate.
        if (_uiSettings is not null)
        {
            ApplySystemAccent();
        }
    }

    /// <summary>Apply a theme using the WinUI <see cref="ElementTheme"/> enum.</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        ApplyTheme(MapTheme(theme));
    }

    public NexCodeTheme GetCurrent() => _current;

    /// <summary>
    /// Reads the Windows system accent color and overrides the app-level
    /// <c>NexCode.Accent.Primary/Hover/Press</c> brushes so buttons and badges follow the
    /// user's chosen accent. Also subscribes to live changes via <see cref="UISettings.ColorValuesChanged"/>.
    /// </summary>
    private void InitializeSystemAccent()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
        ApplySystemAccent();
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        // ColorValuesChanged is raised on a background thread; marshal back to the UI thread
        // before touching Application.Current.Resources and any brushes.
        var queue = _dispatcherQueue;
        if (queue is null)
        {
            return;
        }

        queue.TryEnqueue(ApplySystemAccent);
    }

    private void ApplySystemAccent()
    {
        // In High Contrast the accent brushes intentionally map to system colors via the
        // HighContrast theme dictionary. Remove any app-level override so those brushes
        // show through; setting one here would shadow the accessible system colors.
        if (new AccessibilitySettings().HighContrast)
        {
            ClearAccentOverride("NexCode.Accent.Primary");
            ClearAccentOverride("NexCode.Accent.Hover");
            ClearAccentOverride("NexCode.Accent.Press");
            return;
        }

        var ui = _uiSettings ?? new UISettings();

        var accent = ui.GetColorValue(UIColorType.Accent);
        var hover = ui.GetColorValue(UIColorType.AccentLight1);
        var press = ui.GetColorValue(UIColorType.AccentDark1);

        SetAccentBrush("NexCode.Accent.Primary", accent);
        SetAccentBrush("NexCode.Accent.Hover", hover);
        SetAccentBrush("NexCode.Accent.Press", press);
    }

    private static void SetAccentBrush(string key, Color color)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        // Overriding the brush at the application resource level shadows the theme-dictionary
        // entry of the same key, so all consumers pick up the system accent.
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    private static void ClearAccentOverride(string key)
    {
        Application.Current?.Resources.Remove(key);
    }

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
