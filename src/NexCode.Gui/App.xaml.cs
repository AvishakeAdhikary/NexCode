using System;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NexCode.Gui.BackgroundService;
using NexCode.Gui.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NexCode.Gui;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Spec §29 + §37 — supports headless launch via <c>--background</c> and
/// registers the global summon hotkey.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private GlobalHotkeyService? _hotkeyService;
    private bool _backgroundMode;

    public App()
    {
        InitializeComponent();
        Services = new ServiceCollection()
            .AddNexCodeGuiServices()
            .BuildServiceProvider();
    }

    /// <summary>Process-wide service provider used by <c>MainWindow</c> and pages.</summary>
    public IServiceProvider Services { get; }

    public Window? MainWindow => _window;

    [SupportedOSPlatform("windows10.0.17763.0")]
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _backgroundMode = ParseBackgroundFlag(Environment.GetCommandLineArgs());
        _window = new MainWindow();

        if (_backgroundMode)
        {
            // Headless start: leave the window inactivated so the tray icon
            // hosted by NexCode.Service is the only surface visible at boot.
            // The global hotkey or tray click summons it later.
        }
        else
        {
            _window.Activate();
        }

        _hotkeyService = new GlobalHotkeyService(_window, SummonWindow);
        _hotkeyService.Register();
    }

    private static bool ParseBackgroundFlag(string[] args) =>
        args.Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows10.0.17763.0")]
    private void SummonWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow();
        }

        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            _window.Activate();
        });
    }
}
