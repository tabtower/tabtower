using Microsoft.Win32;

namespace TabTower.Services;

/// <summary>
/// Start with Windows: per-user Run key, no admin. Full state is then
/// restored from the profile via matcher re-bind.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TabTower";

    /// <summary>One-time migration from the Run values of the app's former names ("WinGrid",
    /// then the one in <see cref="LegacyName.Product"/>): if one exists, replace it with a
    /// "TabTower" value pointing at the current exe. Start-with-Windows stays on exactly when
    /// it was on before, and the old build stops being launched at sign-in.</summary>
    public static void MigrateLegacyValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        bool wasEnabled = false;
        foreach (string legacy in new[] { "WinGrid", LegacyName.Product })
        {
            if (key.GetValue(legacy) == null) continue;
            key.DeleteValue(legacy, throwOnMissingValue: false);
            wasEnabled = true;
        }
        if (wasEnabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
    }

    /// <summary>The Run value stores an absolute exe path, so after a reinstall to a new
    /// folder it keeps launching the stale build (or nothing). Rewrite it on startup
    /// whenever it no longer matches the running exe — otherwise an install to a new
    /// folder leaves Windows launching the old build, or nothing at all.</summary>
    public static void RefreshPathIfStale()
    {
        if (Environment.ProcessPath is not { } exe) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue(ValueName) is string current && current != $"\"{exe}\"")
            key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
