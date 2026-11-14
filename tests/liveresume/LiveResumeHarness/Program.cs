// The "still running in another process" line on a session card (0.11.63), offscreen.
// Usage: LiveResumeHarness selftest
// Drives the app's own MainWindow.RefuseLiveResume (what a card click returns when the session
// registry shows the process running and no tab of it is in its window) and
// SessionViewModel.ClearLiveElsewhere (what the next request to open the session does) on a real
// WorkspaceCardView, and reads back what the card draws. No window is shown and no session is touched.
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabTower;
using TabTower.Models;
using TabTower.ViewModels;

internal static class Program
{
    private static int _fail;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) _fail++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args[0] != "selftest") { Console.Error.WriteLine("usage: LiveResumeHarness selftest"); return 2; }
        var app = new App();
        app.InitializeComponent();   // the app-wide resources (converters, styles); Run() is never called

        var ws = new WorkspaceViewModel { Id = 1, Path = @"C:\work\demo", Name = "demo", State = BindState.Disconnected };
        var live = new SessionViewModel { SessionId = "11111111-aaaa", CustomTitle = "live session, no tab found", Status = SessionStatus.Working, StartedAt = DateTime.Now.AddHours(-3) };
        var other = new SessionViewModel { SessionId = "22222222-bbbb", CustomTitle = "another session", Status = SessionStatus.Done, StartedAt = DateTime.Now.AddMinutes(-20) };
        ws.Sessions.Add(live);
        ws.Sessions.Add(other);
        var card = new WorkspaceCardView { DataContext = ws, Width = 460 };
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), Padding = new Thickness(6), Child = card };

        Layout(host);
        Check(Lines(host).Count == 0, "before: no line on the card");

        var refuse = typeof(MainWindow).GetMethod("RefuseLiveResume", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? throw new InvalidOperationException("MainWindow.RefuseLiveResume not found");
        // Exactly what OpenSessionInVscode returns when the registry shows the process running.
        var (sent, reason) = ((bool, string))refuse.Invoke(null, [live])!;
        Check(!sent && reason.Contains("still running") && reason.Contains("second copy"),
              $"refused: nothing sent, and the status line gets the reason (\"{reason}\")");
        Layout(host);
        var lines = Lines(host);
        Check(lines.Count == 1 && lines[0].DataContext == live, $"refused: one line, on that session's row only (got {lines.Count})");
        var line = lines.FirstOrDefault();
        Check(line?.Text.StartsWith("⚠ Still running in another process ") == true && line.Text.EndsWith(": not resumed"),
              $"refused: the line reads \"{line?.Text}\"");
        Check(line?.ToolTip is string tip && tip.Contains("claude --resume") && tip.Contains("same conversation"),
              "refused: the tooltip says why nothing was started");

        live.ClearLiveElsewhere();   // what the next request to open the session does first
        Layout(host);
        Check(Lines(host).Count == 0, "cleared: the next request to open it clears the line");

        refuse.Invoke(null, [live]);
        live.Closed = true;          // the session ended
        Layout(host);
        Check(Lines(host).Count == 0, "closed: a closed session shows no line");

        Console.WriteLine(_fail == 0 ? "all passed" : $"{_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    private static void Layout(FrameworkElement e)
    {
        e.Measure(new Size(480, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
    }

    /// <summary>The lines the card actually draws: visible TextBlocks bound to a session whose text
    /// is the still-running line.</summary>
    private static List<TextBlock> Lines(DependencyObject root)
    {
        var found = new List<TextBlock>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock { Visibility: Visibility.Visible, ActualHeight: > 0 } t && t.DataContext is SessionViewModel &&
                t.Text.StartsWith("⚠ Still running in another process "))
                found.Add(t);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return found;
    }
}
