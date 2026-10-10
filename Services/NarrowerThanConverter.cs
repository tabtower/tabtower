using System.Globalization;
using System.Windows.Data;

namespace TabTower.Services;

/// <summary>
/// True when a laid-out width is below the limit given as the converter parameter. Bind an
/// element's ActualWidth through it in a DataTrigger to rearrange a row that no longer fits on
/// one line (the search row in MainWindow.xaml).
///
/// A width of zero is "not laid out yet", not "narrow": answering true there would arrange
/// every window in its narrow shape for one pass at startup.
/// </summary>
public sealed class NarrowerThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width && width > 0 && parameter is string text &&
           double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) &&
           width < limit;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
