using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Infrastructure;

/// <summary>
/// Selects a DataTemplate for transcript entries based on <see cref="MessageViewModel.Kind"/>.
/// Templates are populated as XAML resources on the consuming page (e.g. <c>ShellPage</c>).
/// </summary>
public sealed class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }
    public DataTemplate? ToolCall { get; set; }
    public DataTemplate? ToolResult { get; set; }
    public DataTemplate? Plan { get; set; }
    public DataTemplate? Todo { get; set; }
    public DataTemplate? Clarify { get; set; }
    public DataTemplate? Checkpoint { get; set; }
    public DataTemplate? Permission { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return SelectTemplateCore(item) ?? base.SelectTemplateCore(item, container);
    }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        if (item is not MessageViewModel msg)
        {
            return Text ?? base.SelectTemplateCore(item);
        }
        return msg.Kind switch
        {
            MessageKind.Plan => Plan ?? Text!,
            MessageKind.Todo => Todo ?? Text!,
            MessageKind.Clarify => Clarify ?? Text!,
            MessageKind.Checkpoint => Checkpoint ?? Text!,
            MessageKind.Permission => Permission ?? Text!,
            MessageKind.ToolCall => ToolCall ?? Text!,
            MessageKind.ToolResult => ToolResult ?? Text!,
            _ => Text!
        };
    }
}
