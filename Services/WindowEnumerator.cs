using System.Diagnostics;
using TabTower.Interop;

namespace TabTower.Services;

public sealed record CandidateWindow(IntPtr Hwnd, string Title, string ProcessName);

public static class WindowEnumerator
{
    /// <summary>
    /// Top-level windows eligible for tiling: visible, titled, not tool windows,
    /// not cloaked (UWP ghosts), and not TabTower itself.
    /// </summary>
    public static List<CandidateWindow> GetCandidates()
    {
        var result = new List<CandidateWindow>();
        int ownPid = Environment.ProcessId;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligible(hwnd, ownPid)) return true;
            result.Add(new CandidateWindow(hwnd, NativeMethods.GetWindowTextSafe(hwnd), GetProcessName(hwnd)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static bool IsEligible(IntPtr hwnd, int ownPid)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetWindowTextSafe(hwnd).Length == 0) return false;
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
        if (NativeMethods.IsCloaked(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownPid) return false;
        return true;
    }

    /// <summary>The Claude desktop app's main window, or Zero when the app is not running. Its
    /// process is `claude.exe`, the same name as the Claude Code CLI, but the CLI owns no
    /// top-level window of its own (a terminal's window belongs to the terminal), so a visible,
    /// titled window under that name is the app. A pop-out session window is one too, so the
    /// one titled exactly "Claude" wins when there are several.</summary>
    public static IntPtr FindClaudeAppWindow()
    {
        var windows = GetCandidates()
            .Where(w => string.Equals(w.ProcessName, "claude", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return (windows.FirstOrDefault(w => w.Title == "Claude") ?? windows.FirstOrDefault())?.Hwnd
               ?? IntPtr.Zero;
    }

    public static int GetProcessId(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
