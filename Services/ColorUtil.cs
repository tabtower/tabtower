using System.Windows.Media;

namespace TabTower.Services;

public static class ColorUtil
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#E53935",
        ["green"] = "#43A047",
        ["orange"] = "#FB8C00",
        ["blue"] = "#1E88E5",
        ["gray"] = "#9E9E9E",
        ["grey"] = "#9E9E9E",
        ["yellow"] = "#FDD835",
        ["purple"] = "#8E24AA",
        ["cyan"] = "#00ACC1",
        ["magenta"] = "#D81B60",
        ["white"] = "#FFFFFF",
        ["black"] = "#000000",
    };

    public static bool TryParse(string value, out Color color)
    {
        color = Colors.Gray;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string s = Named.TryGetValue(value.Trim(), out var hex) ? hex : value.Trim();
        if (!s.StartsWith('#') || s.Length != 7) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(s);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string KnownNames => string.Join(", ", Named.Keys.Where(k => k != "grey"));
}
