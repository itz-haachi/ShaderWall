using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ShaderWallApp;

// The app window. It doesn't render anything itself: it writes the shader to files
// and starts the ShaderWall engine (the console program) to check or run it.
public partial class MainWindow : Window
{
    Process? engine;   // the running wallpaper, if any
    readonly string workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ShaderWall");

    const string StarterShader = """
        // Paste a Shadertoy Image tab here, or try this one
        void mainImage(out vec4 fragColor, in vec2 fragCoord)
        {
            vec2 uv = fragCoord / iResolution.xy;
            vec3 col = 0.5 + 0.5 * cos(iTime + uv.xyx + vec3(0, 2, 4));
            fragColor = vec4(col, 1.0);
        }
        """;

    public MainWindow()
    {
        InitializeComponent();
        ImageBox.Text = StarterShader;
        Closing += (sender, e) => StopWallpaper();   // closing the app stops the wallpaper cleanly
    }

    // ---------- buttons ----------

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        string? exe = FindEngine();
        if (exe == null) return;

        WriteShaderFiles();
        StatusText.Text = "Checking...";
        OutputBox.Text = "";

        var info = new ProcessStartInfo(exe, "--check-only")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using Process p = Process.Start(info)!;
        string output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        OutputBox.Text = output;
        StatusText.Text = p.ExitCode == 0 ? "Check passed" : "Check failed: see the report below";
    }

    void SetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        string? exe = FindEngine();
        if (exe == null) return;

        StopWallpaper();          // only one wallpaper at a time
        WriteShaderFiles();
        OutputBox.Text = "";

        var info = new ProcessStartInfo(exe, $"--listen {SelectedFps()}")
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,    // so we can send "quit"
            RedirectStandardOutput = true,   // so we can show what it prints
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        engine = new Process { StartInfo = info, EnableRaisingEvents = true };
        engine.OutputDataReceived += (s, args) =>
        {
            if (args.Data != null)
                Dispatcher.Invoke(() => OutputBox.AppendText(args.Data + "\n"));
        };
        engine.Exited += (s, args) => Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Not running";
            StopButton.IsEnabled = false;
        });

        engine.Start();
        engine.BeginOutputReadLine();
        StatusText.Text = "Wallpaper running";
        StopButton.IsEnabled = true;
    }

    void Stop_Click(object sender, RoutedEventArgs e) => StopWallpaper();

    // ---------- helpers ----------

    // Ask the engine to quit (it restores the normal wallpaper); force it only if it doesn't respond
    void StopWallpaper()
    {
        if (engine == null || engine.HasExited) return;
        try
        {
            engine.StandardInput.WriteLine("quit");
            if (!engine.WaitForExit(3000))
                engine.Kill();
        }
        catch (InvalidOperationException) { }   // it already exited
        engine = null;
    }

    // The engine reads shader.glsl / common.glsl from its working folder
    void WriteShaderFiles()
    {
        System.IO.Directory.CreateDirectory(workDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(workDir, "shader.glsl"), ImageBox.Text);
        System.IO.File.WriteAllText(System.IO.Path.Combine(workDir, "common.glsl"), CommonBox.Text);
    }

    int SelectedFps()
    {
        string text = (FpsBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "30";
        return int.TryParse(text, out int fps) ? fps : 0;   // "Unlimited" -> 0
    }

    // Walk up from the app's folder until we find the engine project, then use its built .exe
    string? FindEngine()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ShaderWall.csproj")))
            {
                string exe = System.IO.Path.Combine(dir.FullName, "bin", "Debug", "net10.0", "ShaderWall.exe");
                if (System.IO.File.Exists(exe)) return exe;
                break;
            }
            dir = dir.Parent;
        }
        OutputBox.Text = "Couldn't find the ShaderWall engine. Run 'dotnet build' in the ShaderWall folder first.";
        StatusText.Text = "Engine not found";
        return null;
    }
}
