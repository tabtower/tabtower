using TabTower.Interop;

namespace TabTower.Services;

/// <summary>
/// Global WinEvent hooks (no polling): title changes, window destruction,
/// window appearance (create/show, for auto re-bind) and end of a move-drag
/// (for drag-in). Must be started on a thread with a message pump
/// (the UI thread); callbacks arrive there.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _objectProc;   // kept alive for the native hooks
    private readonly NativeMethods.WinEventDelegate _moveProc;
    private IntPtr _objectHook;
    private IntPtr _moveHook;

    public event Action<IntPtr, string>? TitleChanged;
    public event Action<IntPtr>? WindowDestroyed;
    /// <summary>A top-level window was created or shown — candidate for re-bind.</summary>
    public event Action<IntPtr>? WindowAppeared;
    /// <summary>A foreign window finished a move/size drag — candidate for drag-in.</summary>
    public event Action<IntPtr>? MoveSizeEnded;

    public WindowTracker()
    {
        _objectProc = OnObjectEvent;
        _moveProc = OnMoveEvent;
    }

    public void Start()
    {
        if (_objectHook != IntPtr.Zero) return;
        const uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _objectHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_CREATE, NativeMethods.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _objectProc, 0, 0, flags);
        _moveHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _moveProc, 0, 0, flags);
    }

    private void OnObjectEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
            return;

        switch (eventType)
        {
            case NativeMethods.EVENT_OBJECT_DESTROY:
                WindowDestroyed?.Invoke(hwnd);
                break;
            case NativeMethods.EVENT_OBJECT_NAMECHANGE:
                TitleChanged?.Invoke(hwnd, NativeMethods.GetWindowTextSafe(hwnd));
                break;
            case NativeMethods.EVENT_OBJECT_CREATE or NativeMethods.EVENT_OBJECT_SHOW
                when NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) == hwnd:
                WindowAppeared?.Invoke(hwnd);
                break;
        }
    }

    private void OnMoveEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == NativeMethods.OBJID_WINDOW && hwnd != IntPtr.Zero)
            MoveSizeEnded?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (_objectHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_objectHook);
            _objectHook = IntPtr.Zero;
        }
        if (_moveHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_moveHook);
            _moveHook = IntPtr.Zero;
        }
    }
}
