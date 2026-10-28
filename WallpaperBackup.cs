using System.Text;

// Saves the user's normal wallpaper at startup and puts it back on exit.
static class WallpaperBackup
{
    static string savedPath = "";
    static bool saved = false;

    public static void Save()
    {
        var wp = new StringBuilder(260);
        Win32.GetWallpaper(0x0073, (uint)wp.Capacity, wp, 0);   // SPI_GETDESKWALLPAPER
        savedPath = wp.ToString();
        saved = true;
        Console.WriteLine("Saved wallpaper: " + savedPath);
    }

    public static void Restore()
    {
        // Nothing to restore if we never attached (e.g. the shader didn't compile)
        if (!saved) return;

        // SPI_SETDESKWALLPAPER, save + tell the desktop to redraw
        Win32.SetWallpaper(0x0014, 0, savedPath, 0x01 | 0x02);
        Console.WriteLine("Wallpaper restored.");
    }
}
