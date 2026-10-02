using System.IO;
using System.Windows.Threading;

namespace SessionDeck.Services;

/// <summary>
/// Live updates for the tasks file: FileSystemWatcher on the containing folder,
/// filtered to the one file, with a 300ms debounce (editors/exporters fire several events
/// per save). Loading runs off-thread (TasksFileService retries a locked file); the result
/// is delivered on the UI dispatcher. Construct on the UI thread.
/// </summary>
public sealed class TasksFileWatcher : IDisposable
{
    private readonly string _path;
    private readonly Action<TasksLoadResult> _onLoaded;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _fsw;
    private bool _loading;
    private bool _pendingReload;   // a change arrived while a load was in flight
    private bool _disposed;

    public TasksFileWatcher(string path, Action<TasksLoadResult> onLoaded)
    {
        _path = Path.GetFullPath(path);
        _onLoaded = onLoaded;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); BeginLoad(); };

        string? dir = Path.GetDirectoryName(_path);
        if (dir == null || !Directory.Exists(dir))
        {
            // No folder to watch — report once; a later fix requires re-applying the path.
            _dispatcher.BeginInvoke(() =>
                _onLoaded(TasksLoadResult.Error($"The file's folder does not exist: {dir ?? _path}")));
            return;
        }

        _fsw = new FileSystemWatcher(dir, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _fsw.Changed += OnFsEvent;
        _fsw.Created += OnFsEvent;
        _fsw.Deleted += OnFsEvent;
        // Atomic writers replace via rename — the target name only shows up in Renamed.
        _fsw.Renamed += (_, _) => QueueReload();
        _fsw.Error += (_, _) => QueueReload();   // buffer overflow etc. — just re-read
        _fsw.EnableRaisingEvents = true;

        BeginLoad();   // initial read
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => QueueReload();

    private void QueueReload()
    {
        if (_disposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void BeginLoad()
    {
        if (_disposed) return;
        if (_loading) { _pendingReload = true; return; }
        _loading = true;
        Task.Run(() =>
        {
            var result = TasksFileService.LoadWithRetry(_path);
            _dispatcher.BeginInvoke(() =>
            {
                _loading = false;
                if (_disposed) return;
                _onLoaded(result);
                if (_pendingReload) { _pendingReload = false; BeginLoad(); }
            });
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce.Stop();
        _fsw?.Dispose();
        _fsw = null;
    }
}
