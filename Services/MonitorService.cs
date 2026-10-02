using TabTower.Interop;

namespace TabTower.Services;

public sealed record MonitorEntry(int Index, string Device, RECT Bounds, RECT WorkArea, bool Primary)
{
    public string DisplayName => $"Monitor {Index + 1}{(Primary ? " (primary)" : "")}";
}

public static class MonitorService
{
    /// <summary>Monitors sorted primary-first then left-to-right, so "monitor 1" is always the primary.</summary>
    public static List<MonitorEntry> GetMonitors()
    {
        var result = new List<MonitorEntry>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    result.Add(new MonitorEntry(result.Count, mi.szDevice, mi.rcMonitor, mi.rcWork,
                        (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
                }
                return true;
            }, IntPtr.Zero);
        return result
            .OrderByDescending(m => m.Primary)
            .ThenBy(m => m.Bounds.Left)
            .Select((m, i) => m with { Index = i })
            .ToList();
    }
}
