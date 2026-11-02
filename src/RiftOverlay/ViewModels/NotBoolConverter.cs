using System.Globalization;
using System.Windows.Data;

namespace RiftOverlay.ViewModels;

/// <summary>
/// Inverts a boolean — used to disable a button while its work is in flight, so the same "busy"
/// flag drives both the spinner state and the control.
/// </summary>
public sealed class NotBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}
