using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShaderWallApp;

// One saved shader. Properties without [JsonIgnore] are written into library.json.
// INotifyPropertyChanged lets the Library cards update when a value changes (e.g. which one is active).
public class SavedShader : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string Common { get; set; } = "";

    DateTime saved;
    public DateTime Saved
    {
        get => saved;
        set { saved = value; Changed(nameof(Saved)); Changed(nameof(SavedText)); }
    }

    string? thumbnailPath;
    public string? ThumbnailPath
    {
        get => thumbnailPath;
        set { thumbnailPath = value; Changed(nameof(ThumbnailPath)); Changed(nameof(Thumbnail)); }
    }

    bool isActive;
    [JsonIgnore]
    public bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value) return;
            isActive = value;
            Changed(nameof(IsActive));
            Changed(nameof(ActiveVisibility));
        }
    }

    [JsonIgnore] public Visibility ActiveVisibility => isActive ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore] public string SavedText => "Saved " + Saved.ToString("d MMM yyyy");

    // Loads the thumbnail fully into memory (OnLoad), so the file isn't locked and can be replaced later
    [JsonIgnore]
    public ImageSource? Thumbnail
    {
        get
        {
            if (thumbnailPath == null || !File.Exists(thumbnailPath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(thumbnailPath);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public override string ToString() => Name;
}

// Reads and writes %AppData%\ShaderWall\library.json (plus thumbnails and the API key)
public static class ShaderLibrary
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShaderWall");
    public static readonly string FilePath = Path.Combine(Folder, "library.json");
    public static readonly string ThumbFolder = Path.Combine(Folder, "thumbs");
    public static readonly string ApiKeyPath = Path.Combine(Folder, "apikey.txt");

    public static List<SavedShader> Load()
    {
        if (!File.Exists(FilePath)) return new List<SavedShader>();
        try
        {
            return JsonSerializer.Deserialize<List<SavedShader>>(File.ReadAllText(FilePath)) ?? new List<SavedShader>();
        }
        catch (JsonException)
        {
            // Damaged file: keep a copy instead of overwriting it, and start empty
            File.Copy(FilePath, FilePath + ".damaged", overwrite: true);
            return new List<SavedShader>();
        }
    }

    public static void Save(IEnumerable<SavedShader> shaders)
    {
        Directory.CreateDirectory(Folder);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(shaders, options));
    }
}
