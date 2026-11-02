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

    // When the code changes, the "needs attention" result must be worked out again
    string image = "";
    public string Image
    {
        get => image;
        set { image = value; attentionChecked = false; AttentionChanged(); }
    }

    string common = "";
    public string Common
    {
        get => common;
        set { common = value; attentionChecked = false; AttentionChanged(); }
    }

    bool favourite;
    public bool Favourite
    {
        get => favourite;
        set { favourite = value; Changed(nameof(Favourite)); Changed(nameof(StarGlyph)); }
    }

    // Filled star or outline star (Segoe Fluent Icons)
    [JsonIgnore] public string StarGlyph => favourite ? "\uE735" : "\uE734";

    // "Needs attention": the first problem or warning from the compatibility check, or "" if none.
    // Worked out once and remembered (attentionChecked), because the check reads the whole shader.
    string attention = "";
    bool attentionChecked;
    [JsonIgnore]
    public string AttentionText
    {
        get
        {
            if (!attentionChecked)
            {
                attention = "";
                CheckReport report = CompatibilityChecker.CheckSource(image, common, null);
                foreach (var (mark, text) in report.Items)
                {
                    if (mark == "OK") continue;
                    int colon = text.IndexOf(':');
                    attention = colon > 0 ? text[..colon] : text;
                    break;
                }
                attentionChecked = true;
            }
            return attention;
        }
    }
    [JsonIgnore] public bool NeedsAttention => AttentionText != "";
    [JsonIgnore] public Visibility AttentionVisibility => NeedsAttention ? Visibility.Visible : Visibility.Collapsed;

    void AttentionChanged()
    {
        Changed(nameof(AttentionText));
        Changed(nameof(NeedsAttention));
        Changed(nameof(AttentionVisibility));
    }

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
