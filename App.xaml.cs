using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Interop;
using TabTower.Interop;

namespace TabTower
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Caption grey — a shade above the card background (#1E1E1E), matching a workspace card header.</summary>
        private const int CaptionColorRef = 0x00252525;
        private const int CaptionTextColorRef = 0x00FFFFFF;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // One class handler paints the native title bar of EVERY window (main window and
            // every dialog, including ones added later) instead of each one wiring it itself.
            // Loaded is used rather than SourceInitialized because only Loaded is a routed
            // event, and by then the HWND exists.
            EventManager.RegisterClassHandler(
                typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAnyWindowLoaded));
        }

        private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window w) return;
            ApplyDarkTitleBar(new WindowInteropHelper(w).Handle);
        }

        /// <summary>
        /// The main window calls this from SourceInitialized as well — earlier than the class
        /// handler above, so the caption is never painted light for a frame on startup.
        /// </summary>
        internal static void ApplyDarkTitleBar(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            NativeMethods.ApplyDarkTitleBar(hwnd, CaptionColorRef, CaptionTextColorRef);
        }
    }

}
