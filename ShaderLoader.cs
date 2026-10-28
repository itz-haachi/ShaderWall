using System.Text.Json;
using System.Text.RegularExpressions;

// Everything we need from a downloaded Shadertoy shader
record ShaderSource(string Name, string Author, string ImageCode, string CommonCode,
                    List<string> Tabs, bool MultiPass, bool UsesTextures);

// Loads a shader from shadertoy.com using its official API
// (or from a saved API reply, for testing without a key).
static class ShaderLoader
{
    static readonly HttpClient http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShaderWall/0.1");
        return client;
    }

    // "https://www.shadertoy.com/view/w3Xfz8" -> "w3Xfz8"
    public static string? ExtractId(string url)
    {
        Match m = Regex.Match(url, @"shadertoy\.com/(?:view|embed)/([A-Za-z0-9]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // Real mode: download the reply from the API, then read it
    public static async Task<ShaderSource?> FromUrlAsync(string url, string apiKey)
    {
        string? id = ExtractId(url);
        if (id == null)
        {
            Console.WriteLine("That doesn't look like a Shadertoy link.");
            return null;
        }

        string apiUrl = $"https://www.shadertoy.com/api/v1/shaders/{id}?key={apiKey}";
        Console.WriteLine("Fetching shader " + id + " ...");

        string json;
        try
        {
            json = await http.GetStringAsync(apiUrl);
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("Download failed: " + e.Message);
            return null;
        }

        return ParseReply(json);
    }

    // Test mode: read a saved API reply from a file instead of downloading it
    public static ShaderSource? FromJsonFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("File not found: " + path);
            return null;
        }
        Console.WriteLine("TEST MODE: reading saved API reply " + path);
        return ParseReply(File.ReadAllText(path));
    }

    // Read the API's JSON reply. Both modes end up here.
    static ShaderSource? ParseReply(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            Console.WriteLine("The reply wasn't valid JSON.");
            return null;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;

            // On failure the API answers with something like {"Error":"..."} instead of {"Shader":{...}}
            if (!root.TryGetProperty("Shader", out JsonElement shader))
            {
                Console.WriteLine("Shadertoy said: " + json.Trim());
                Console.WriteLine("This shader may not be shared as \"Public + API\". Paste its code into shader.glsl instead.");
                return null;
            }

            JsonElement info = shader.GetProperty("info");
            string name = Text(info, "name", "Untitled");
            string author = Text(info, "username", "unknown");

            string image = "", common = "";
            var tabs = new List<string>();
            bool multiPass = false, usesTextures = false;

            foreach (JsonElement pass in shader.GetProperty("renderpass").EnumerateArray())
            {
                string type = Text(pass, "type", "");
                string code = Text(pass, "code", "");
                tabs.Add(Text(pass, "name", type));

                if (type == "image")
                {
                    image = code;
                    if (pass.TryGetProperty("inputs", out JsonElement inputs) && inputs.GetArrayLength() > 0)
                        usesTextures = true;
                }
                else if (type == "common")
                {
                    common = code;
                }
                else
                {
                    multiPass = true;   // Buffer A-D, Cube A, Sound
                }
            }

            if (image == "")
            {
                Console.WriteLine("No Image tab found in the reply.");
                return null;
            }

            return new ShaderSource(name, author, image, common, tabs, multiPass, usesTextures);
        }
    }

    // Read a text field from JSON, or use a fallback if it's missing
    static string Text(JsonElement el, string property, string fallback)
    {
        return el.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;
    }
}
