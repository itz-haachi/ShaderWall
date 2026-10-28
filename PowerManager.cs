using System.Text;

// Decides whether the wallpaper can be seen, so rendering can pause when it can't.
static class PowerManager
{
    public static bool IsWallpaperHidden()
    {
        IntPtr front = Win32.GetForegroundWindow();
        if (front == IntPtr.Zero) return false;

        // If the desktop itself is in front, the wallpaper is visible
        var name = new StringBuilder(256);
        Win32.GetClassName(front, name, name.Capacity);
        string cls = name.ToString();
        if (cls == "Progman" || cls == "WorkerW") return false;

        // A maximized window hides the wallpaper
        if (Win32.IsZoomed(front)) return true;

        // A fullscreen window (games, videos) covers the whole screen
        Win32.GetWindowRect(front, out Win32.RECT r);
        int screenW = Win32.GetSystemMetrics(0);
        int screenH = Win32.GetSystemMetrics(1);
        return r.Left <= 0 && r.Top <= 0 && r.Right >= screenW && r.Bottom >= screenH;
    }
}
