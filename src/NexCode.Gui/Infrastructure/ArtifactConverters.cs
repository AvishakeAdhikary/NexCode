using Microsoft.UI.Xaml.Data;
using NexCode.Gui.ViewModels;

namespace NexCode.Gui.Infrastructure;

/// <summary>Converts a generic <c>MessageViewModel.Artifact</c> object to a typed artifact view model.</summary>
public sealed class PlanArtifactConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value as PlanViewModel;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value!;
}

public sealed class TodoArtifactConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value as TodoListViewModel;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value!;
}

public sealed class ClarifyArtifactConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value as ClarifyQuestionViewModel;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value!;
}

public sealed class CheckpointArtifactConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value as CheckpointViewModel;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value!;
}

public sealed class PermissionArtifactConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) => value as PermissionPromptViewModel;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value!;
}
