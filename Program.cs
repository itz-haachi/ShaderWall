using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;

// ShaderWall entry point: read settings, create the window, run it.

var settings = new NativeWindowSettings()
{
    ClientSize = new Vector2i(1280, 720),
    Title = "ShaderWall"
};

// FPS cap: 30 by default, or pass a number: dotnet run -- 60   (0 = unlimited)
int fpsCap = args.Length > 0 ? int.Parse(args[0]) : 30;
Console.WriteLine("FPS cap: " + (fpsCap == 0 ? "unlimited" : fpsCap.ToString()));
var gameSettings = new GameWindowSettings() { UpdateFrequency = fpsCap };
using var window = new ShaderWindow(gameSettings, settings);

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
