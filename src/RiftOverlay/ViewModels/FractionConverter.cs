using System.Globalization;
using System.Windows.Data;

namespace RiftOverlay.ViewModels;

/// <summary>
/// Turns a measured size into a fraction of it, so a panel can be capped relative to the overlay
/// rather than at a fixed number of pixels.
/// <para>
/// The objective row used to stop wrapping at a hard-coded 720. That number was chosen against one
/// screen: on a 2560-wide client it left the row hugging the left third, and at 1280 it let the row
/// run most of the way across and collide with panels on the right. A fraction of the actual
/// overlay width behaves the same way at every resolution, which is the same reasoning that put
/// panel positions in fractions rather than pixels.
/// </para>
/// </summary>
public sealed class FractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double size || double.IsNaN(size) || size <= 0)
        {
            // Before the first layout pass the host has no width yet. Returning "no limit" lets the
            // panel measure naturally instead of collapsing to nothing on the first frame.
            return double.PositiveInfinity;
        }

        var fraction = parameter is string text &&
                       double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1.0;

        return size * fraction;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
