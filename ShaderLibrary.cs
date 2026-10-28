using System.IO;
using System.Text.Json;

namespace ShaderWallApp;

// One saved shader. The properties are what gets written into library.json.
public class SavedShader
{
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string Common { get; set; } = "";
    public DateTime Saved { get; set; }

    public override string ToString() => Name;   // what the list shows
}

// Reads and writes the library file: %AppData%\ShaderWall\library.json
public static class ShaderLibrary
{
    static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShaderWall");
    public static readonly string FilePath = Path.Combine(Folder, "library.json");

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
