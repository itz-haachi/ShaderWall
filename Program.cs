using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;

// ShaderWall entry point.
//   dotnet run                                   -> shader.glsl, 30 FPS
//   dotnet run -- 60                             -> shader.glsl, 60 FPS (0 = unlimited)
//   dotnet run -- https://www.shadertoy.com/view/XXXXXX     -> download that shader
//   dotnet run -- https://www.shadertoy.com/view/XXXXXX 60  -> both
//   dotnet run -- samples/sample.json            -> TEST MODE: a saved API reply, no key needed
//   add --show-fps to any of these to print the frame rate every second
//   --check-only  : check + compile in a hidden window, then exit (exit code 0 = OK, 1 = problem)
//   --listen      : obey "quit", "pause" and "resume" lines from another program (used by the ShaderWall app)

int fpsCap = 30;
string? url = null;
string? jsonFile = null;
bool showFps = false;
bool checkOnly = false;
bool listen = false;
foreach (string a in args)
{
    if (a == "--show-fps") showFps = true;
    else if (a == "--check-only") checkOnly = true;
    else if (a == "--listen") listen = true;
    else if (int.TryParse(a, out int n)) fpsCap = n;
    else if (a.Contains("shadertoy.com")) url = a;
    else if (a.EndsWith(".json")) jsonFile = a;
}
Console.WriteLine("FPS cap: " + (fpsCap == 0 ? "unlimited" : fpsCap.ToString()));

// 1. Get the shader code: from a link, a saved API reply, or the local files
string imageCode, commonCode;
bool? multiPass = null;   // only known when the shader comes from the API

if (url != null || jsonFile != null)
{
    ShaderSource? src;
    if (jsonFile != null)
    {
        src = ShaderLoader.FromJsonFile(jsonFile);
    }
    else
    {
        if (!File.Exists("apikey.txt"))
        {
            Console.WriteLine("Put your Shadertoy API key in a file called apikey.txt first.");
            return;
        }
        string apiKey = File.ReadAllText("apikey.txt").Trim();
        src = await ShaderLoader.FromUrlAsync(url!, apiKey);
    }
    if (src == null) return;

    Console.WriteLine($"Loaded \"{src.Name}\" by {src.Author}  (tabs: {string.Join(", ", src.Tabs)})");
    imageCode = src.ImageCode;
    commonCode = src.CommonCode;
    multiPass = src.MultiPass;
}
else
{
    if (!File.Exists("shader.glsl"))
    {
        Console.WriteLine("No shader.glsl found. Paste a shader's Image tab into shader.glsl first.");
        return;
    }
    imageCode = File.ReadAllText("shader.glsl");
    commonCode = File.Exists("common.glsl") ? File.ReadAllText("common.glsl") : "";
}

// 2. Check it before running (compiling is checked once the window exists)
CheckReport report = CompatibilityChecker.CheckSource(imageCode, commonCode, multiPass);
report.Print();
if (!report.CanRun)
{
    Environment.ExitCode = 1;
    return;
}

string fragmentCode = ShaderBuilder.Build(imageCode, commonCode);

// 3. Create the window
var settings = new NativeWindowSettings()
{
    ClientSize = new Vector2i(1280, 720),
    Title = "ShaderWall",
    StartVisible = !checkOnly,  // check-only uses an invisible window
    StartFocused = false        // don't steal focus from the app (or whatever you're using)
};
var gameSettings = new GameWindowSettings() { UpdateFrequency = fpsCap };
using var window = new ShaderWindow(gameSettings, settings, fragmentCode);
window.showFps = showFps;
window.checkOnly = checkOnly;

// When started by the ShaderWall app: a background thread waits for a "quit" line
if (listen)
{
    var listener = new Thread(() =>
    {
        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null || line.Trim() == "quit")   // null = the app closed
            {
                window.quitRequested = true;
                break;
            }
            if (line.Trim() == "pause") window.paused = true;
            if (line.Trim() == "resume") window.paused = false;
        }
    });
    listener.IsBackground = true;   // don't keep the program alive just for this thread
    listener.Start();
}

// Ctrl+C asks the app to close cleanly instead of killing it
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    window.quitRequested = true;
};

// Windows' sleep timer normally ticks every ~15.6 ms, which makes the FPS cap undershoot.
// Ask for 1 ms precision while ShaderWall runs, and give it back afterwards.
Win32.timeBeginPeriod(1);
window.Run();
Win32.timeEndPeriod(1);
