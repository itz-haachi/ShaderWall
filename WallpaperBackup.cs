using System.Text;

// Saves the user's normal wallpaper at startup and puts it back on exit.
static class WallpaperBackup
{
    static string savedPath = "";

    public static void Save()
    {
        var wp = new StringBuilder(260);
        Win32.GetWallpaper(0x0073, (uint)wp.Capacity, wp, 0);   // SPI_GETDESKWALLPAPER
        savedPath = wp.ToString();
        Console.WriteLine("Saved wallpaper: " + savedPath);
    }

    public static void Restore()
    {
        // SPI_SETDESKWALLPAPER, save + tell the desktop to redraw
        Win32.SetWallpaper(0x0014, 0, savedPath, 0x01 | 0x02);
        Console.WriteLine("Wallpaper restored.");
    }
}
