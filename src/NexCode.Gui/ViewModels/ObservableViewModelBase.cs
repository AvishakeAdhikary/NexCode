using CommunityToolkit.Mvvm.ComponentModel;

namespace NexCode.Gui.ViewModels;

/// <summary>
/// Base class for all NexCode GUI view models. Inherits MVVM Toolkit's
/// <see cref="ObservableObject"/> for source-generated property change support.
/// Reserved for shared cross-cutting behavior (telemetry hooks, DispatcherQueue
/// helpers, etc.) that we'll layer in via subsequent slices.
/// </summary>
public abstract partial class ObservableViewModelBase : ObservableObject
{
    /// <summary>
    /// Optional human-readable name used by diagnostics and the live debugger
    /// pane to identify a view model instance. Subclasses override this.
    /// </summary>
    public virtual string DiagnosticName => GetType().Name;
}
