using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RiftOverlay.ViewModels;

/// <summary>
/// Collapses an element unless every bound condition is true. Panels have two independent reasons
/// to disappear — no game in progress, or the user switched the panel off — and this keeps both
/// in the binding instead of duplicating the combination as view-model properties.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
