using CommunityToolkit.Mvvm.ComponentModel;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// Sidebar entry for a project / workspace folder. Bound by the navigation rail in <c>ShellPage</c>.
/// </summary>
public sealed partial class ProjectViewModel : ObservableViewModelBase
{
    public ProjectViewModel()
        : this(string.Empty, string.Empty, DateTimeOffset.MinValue)
    {
    }

    public ProjectViewModel(string directoryPath, string displayName, DateTimeOffset lastActive)
    {
        _directoryPath = directoryPath;
        _displayName = displayName;
        _lastActive = lastActive;
    }

    [ObservableProperty]
    private string _directoryPath;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private DateTimeOffset _lastActive;

    [ObservableProperty]
    private bool _isActive;

    public string Subtitle => DirectoryPath;
}
