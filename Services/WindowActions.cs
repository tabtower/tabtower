using System.Diagnostics;
using System.IO;
using TabTower.Interop;

namespace TabTower.Services;

public static class WindowActions
{
    /// <summary>Open VSCode on a workspace folder (card click when no window is bound).
    /// The new window auto-binds via the WindowAppeared tracker event.
    /// Must go through the CLI shim (bin\code.cmd): launching Code.exe directly fails
    /// silently to open a window when another VSCode instance is already running
    /// (verified 2026-07-19).</summary>
    public static bool LaunchVsCode(string workspacePath)
    {
        try
        {
            string shim = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "bin", "code.cmd");
            string cli = File.Exists(shim) ? shim : "code";   // fall back to PATH
            var psi = new ProcessStartInfo("cmd.exe")
            {
                // cmd /c ""shim" "path"" — outer quotes protect the two quoted args.
                Arguments = $"/c \"\"{cli}\" \"{workspacePath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Focus: bring the real window to front at its current position.</summary>
    public static void Focus(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    /// <summary>Graceful close (WM_CLOSE) — same as clicking the window's X, so VSCode
    /// gets to run its normal shutdown (save prompts etc.).</summary>
    public static void Close(IntPtr hwnd)
        => NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Pin: move the real window to the stage rect and activate it.</summary>
    public static void MoveTo(IntPtr hwnd, RECT rect)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    /// <summary>Stage "full" = a REAL maximize (feedback 2026-07-19): sizing a normal
    /// window to the work area leaves it in Normal state — Win11 border gaps, resizable
    /// edges, wrong maximize-button state. Park on the target monitor first so the
    /// maximize lands there.</summary>
    public static void MaximizeOn(IntPtr hwnd, RECT work)
    {
        if (NativeMethods.IsIconic(hwnd) || NativeMethods.IsZoomed(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, work.Left, work.Top, work.Width, work.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        NativeMethods.SetForegroundWindow(hwnd);
    }
}
