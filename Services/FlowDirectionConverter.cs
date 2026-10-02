using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TabTower.Services;

/// <summary>
/// Text that starts (first strong character) with Hebrew/Arabic renders right-aligned in
/// RTL direction; anything else stays LTR (issue 2026-07-19). Bind a TextBlock's
/// FlowDirection to its own text through this converter.
/// </summary>
public sealed class FlowDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            foreach (char c in s)
            {
                if (IsRtl(c)) return FlowDirection.RightToLeft;
                if (char.IsAsciiLetter(c)) return FlowDirection.LeftToRight;
            }
        }
        return FlowDirection.LeftToRight;
    }

    private static bool IsRtl(char c) =>
        c is >= '֐' and <= '׿'      // Hebrew block U+0590-U+05FF (public-gate: allow, the range bounds are functional)
          or >= '؀' and <= 'ۿ'      // Arabic
          or >= 'יִ' and <= 'ﭏ';     // Hebrew presentation forms

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
