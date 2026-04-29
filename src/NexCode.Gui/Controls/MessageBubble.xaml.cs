using Markdig;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Controls;

public sealed partial class MessageBubble : UserControl
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(MessageViewModel),
        typeof(MessageBubble),
        new PropertyMetadata(null, OnMessageChanged));

    public MessageBubble()
    {
        InitializeComponent();
    }

    public MessageViewModel? Message
    {
        get => (MessageViewModel?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MessageBubble bubble)
        {
            if (e.OldValue is MessageViewModel oldVm)
            {
                oldVm.PropertyChanged -= bubble.OnMessagePropertyChanged;
            }
            if (e.NewValue is MessageViewModel newVm)
            {
                newVm.PropertyChanged += bubble.OnMessagePropertyChanged;
            }
            bubble.Render();
        }
    }

    private void OnMessagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageViewModel.Content))
        {
            DispatcherQueue.TryEnqueue(Render);
        }
    }

    private void Render()
    {
        if (Message is null)
        {
            UserBubble.Visibility = Visibility.Collapsed;
            AssistantBubble.Visibility = Visibility.Collapsed;
            SystemBubble.Visibility = Visibility.Collapsed;
            return;
        }

        switch (Message.Role)
        {
            case MessageRole.User:
                UserBubble.Visibility = Visibility.Visible;
                AssistantBubble.Visibility = Visibility.Collapsed;
                SystemBubble.Visibility = Visibility.Collapsed;
                UserContentText.Text = Message.Content;
                break;
            case MessageRole.Assistant:
                AssistantBubble.Visibility = Visibility.Visible;
                UserBubble.Visibility = Visibility.Collapsed;
                SystemBubble.Visibility = Visibility.Collapsed;
                RenderAssistantMarkdown(Message.Content);
                break;
            default:
                SystemBubble.Visibility = Visibility.Visible;
                UserBubble.Visibility = Visibility.Collapsed;
                AssistantBubble.Visibility = Visibility.Collapsed;
                SystemContentText.Text = Message.Content;
                break;
        }
    }

    private void RenderAssistantMarkdown(string markdownText)
    {
        AssistantContent.Blocks.Clear();
        // We render the markdown as plain text in this slice — a richer Markdig->FlowDocument
        // bridge ships in slice 0014. Code fences are still preserved by ToPlainText.
        var plain = string.IsNullOrEmpty(markdownText) ? string.Empty : Markdown.ToPlainText(markdownText);
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = plain });
        AssistantContent.Blocks.Add(paragraph);
    }
}
