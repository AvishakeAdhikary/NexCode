using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NexCode.Gui.ViewModels.Pages;

namespace NexCode.Gui.Pages.Settings;

/// <summary>
/// Keyboard binding table (spec §20.2). Editing pops a small <see cref="ContentDialog"/>
/// recorder; the recorder captures the next keypress and writes it back to the row.
/// </summary>
public sealed partial class KeyboardSettingsPage : Page
{
    public KeybindingsViewModel ViewModel { get; } = new();

    public KeyboardSettingsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => RunRecorder(sender, isSecondary: false);
    private void AddSecondaryButton_Click(object sender, RoutedEventArgs e) => RunRecorder(sender, isSecondary: true);

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: KeybindingRowViewModel row })
        {
            ViewModel.ResetCommand.Execute(row);
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: KeybindingRowViewModel row })
        {
            ViewModel.RemoveCommand.Execute(row);
        }
    }

    private async void RunRecorder(object sender, bool isSecondary)
    {
        if (sender is not Button { Tag: KeybindingRowViewModel row })
        {
            return;
        }

        var label = new TextBlock
        {
            Text = $"Press the new shortcut for: {row.Action}",
            TextWrapping = TextWrapping.Wrap,
        };
        var captured = new TextBlock
        {
            Text = "(waiting...)",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(label);
        stack.Children.Add(captured);

        var dialog = new ContentDialog
        {
            Title = "Record binding",
            Content = stack,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        string capturedString = string.Empty;
        dialog.KeyDown += (_, ka) =>
        {
            capturedString = ka.Key.ToString();
            captured.Text = capturedString;
            ka.Handled = true;
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(capturedString))
        {
            if (isSecondary)
            {
                row.Secondary = capturedString;
            }
            else
            {
                row.Primary = capturedString;
            }

            ViewModel.StatusMessage = $"Updated {row.Action} → {capturedString}.";
        }
    }
}
