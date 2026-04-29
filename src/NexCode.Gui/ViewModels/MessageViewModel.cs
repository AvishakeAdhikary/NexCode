using CommunityToolkit.Mvvm.ComponentModel;

namespace NexCode.Gui.ViewModels;

public enum MessageRole
{
    User,
    Assistant,
    Tool,
    System
}

/// <summary>
/// Coarse classification used by the transcript template selector to map a message
/// to the right artifact UserControl (text bubble, plan card, todo card, etc.).
/// </summary>
public enum MessageKind
{
    Text,
    ToolCall,
    ToolResult,
    Plan,
    Todo,
    Clarify,
    Checkpoint,
    Permission
}

/// <summary>
/// A single transcript entry. Streaming assistant turns mutate <see cref="Content"/>
/// repeatedly; bound text controls observe via INotifyPropertyChanged.
/// </summary>
public sealed partial class MessageViewModel : ObservableViewModelBase
{
    public MessageViewModel(MessageRole role, MessageKind kind, string content)
    {
        Role = role;
        Kind = kind;
        _content = content;
        Timestamp = DateTimeOffset.Now;
    }

    public MessageRole Role { get; }

    public MessageKind Kind { get; }

    public DateTimeOffset Timestamp { get; }

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>Optional artifact view model (Plan/Todo/Clarify/Checkpoint/Permission) attached to this message.</summary>
    [ObservableProperty]
    private object? _artifact;

    /// <summary>Optional tool call/result metadata, e.g. tool name displayed inline.</summary>
    [ObservableProperty]
    private string? _toolName;

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
}
