using System.Runtime.InteropServices;
using System.Text;

// Decides whether the wallpaper can be seen, so rendering can pause when it can't.
static class PowerManager
{
    // ownWindow = the wallpaper window itself, which must never count as "covering" the wallpaper
    public static bool IsWallpaperHidden(IntPtr ownWindow)
    {
        IntPtr front = Win32.GetForegroundWindow();
        if (front == IntPtr.Zero) return false;
        if (front == ownWindow) return false;

        // If the desktop itself is in front, the wallpaper is visible
        var name = new StringBuilder(256);
        Win32.GetClassName(front, name, name.Capacity);
        string cls = name.ToString();
        if (cls == "Progman" || cls == "WorkerW") return false;

        // Only a window on the SAME monitor as the wallpaper can hide it.
        // (With a laptop + external screen, a maximized window on the other screen doesn't count.)
        IntPtr wallpaperMonitor = Win32.MonitorFromWindow(ownWindow, Win32.MONITOR_DEFAULTTOPRIMARY);
        IntPtr frontMonitor = Win32.MonitorFromWindow(front, Win32.MONITOR_DEFAULTTONEAREST);
        if (frontMonitor != wallpaperMonitor) return false;

        // A maximized window hides the wallpaper
        if (Win32.IsZoomed(front)) return true;

        // A fullscreen window (games, videos) covers that whole monitor
        var monitor = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(frontMonitor, ref monitor)) return false;
        Win32.GetWindowRect(front, out Win32.RECT r);
        Win32.RECT m = monitor.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
