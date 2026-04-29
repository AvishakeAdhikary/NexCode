using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NexCode.Gui.Infrastructure;

/// <summary>
/// Standard bool -> Visibility conversion. Used widely across cards/overlays.
/// Set <c>Invert</c> via the <c>parameter</c> argument ("invert") to flip the mapping.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility v && v == Visibility.Visible;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }
        return isVisible;
    }
}
