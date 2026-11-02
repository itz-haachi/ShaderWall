using System.IO;
using System.Text.Json;

namespace ShaderWallApp;

// Things the app remembers between runs: %AppData%\ShaderWall\settings.json
public class AppSettings
{
    public int FpsCap { get; set; } = 30;
    public int RenderScale { get; set; } = 100;   // percent of full resolution

    // Slideshow: move to the next library shader on these events
    public bool SlideshowOnSleep { get; set; }   // switch as the PC goes to sleep
    public bool SlideshowOnLock { get; set; }    // switch as the PC is locked
    public int SlideshowEveryMinutes { get; set; }   // 0 = off
    public bool SlideshowRandom { get; set; }

    // The last wallpaper, so it can come back when Windows starts
    public bool WallpaperOn { get; set; }
    public string LastName { get; set; } = "";
    public string LastImage { get; set; } = "";
    public string LastCommon { get; set; } = "";

    static readonly string FilePath = Path.Combine(ShaderLibrary.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (JsonException) { }   // damaged file: just use the defaults
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(ShaderLibrary.Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
