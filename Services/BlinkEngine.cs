using System.Windows.Threading;

namespace TabTower.Services;

/// <summary>Anything with a status-driven blinking border (session cards today).</summary>
public interface IBlinkable
{
    bool BlinkActive { get; }
    int BlinkIntervalMs { get; }
    bool AltPhase { get; set; }
}

/// <summary>
/// One shared DispatcherTimer for all blinking borders — never a timer per card.
/// Each target's phase is derived from the wall clock and its own interval, so different
/// intervals coexist on the same tick.
/// </summary>
public sealed class BlinkEngine
{
    private readonly Func<IEnumerable<IBlinkable>> _targets;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public BlinkEngine(Func<IEnumerable<IBlinkable>> targets)
    {
        _targets = targets;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Call after any change that can start/stop blinking (status changes, acknowledge, add/remove).</summary>
    public void Refresh()
    {
        bool any = false;
        foreach (var t in _targets())
        {
            if (t.BlinkActive) any = true;
            else t.AltPhase = false;
        }
        if (any && !_timer.IsEnabled) _timer.Start();
        else if (!any && _timer.IsEnabled) _timer.Stop();
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        bool any = false;
        foreach (var t in _targets())
        {
            if (!t.BlinkActive)
            {
                t.AltPhase = false;
                continue;
            }
            any = true;
            int interval = Math.Max(100, t.BlinkIntervalMs);
            t.AltPhase = now / interval % 2 == 1;
        }
        if (!any) _timer.Stop();
    }
}
