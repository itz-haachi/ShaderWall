using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Wpf;

namespace ShaderWallApp;

// The app window. It shows a live preview itself (GLWpfControl), and starts the
// ShaderWall engine (the console program) to run the shader as the wallpaper.
public partial class MainWindow : Window
{
    Process? engine;   // the running wallpaper, if any
    readonly string workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ShaderWall");

    // Live preview state. GL calls only work inside Preview_OnRender, so new code waits in pendingCode.
    string? pendingCode;
    int previewProgram;
    int previewVao;
    float previewTime;

    // Saved shaders. An ObservableCollection tells the ListBox to refresh itself when items change.
    readonly ObservableCollection<SavedShader> library = new(ShaderLibrary.Load());

    bool previewRunning;    // only true after Check + preview, and false while the wallpaper runs
    double infoTimer;       // update the info text 4 times a second, not every frame

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

        // Start the preview's OpenGL context (4.3, the same version the wallpaper uses)
        Preview.Start(new GLWpfControlSettings { MajorVersion = 4, MinorVersion = 3 });
        PreviewInfo.Text = "Press Check + preview to run the preview";

        LibraryList.ItemsSource = library;
        Closing += (sender, e) => StopWallpaper();   // closing the app stops the wallpaper cleanly
    }

    // ---------- buttons ----------

    void Check_Click(object sender, RoutedEventArgs e) => RunCheck();

    // Step 1 (no GPU): read the code and report problems.
    // Step 2 (GPU): hand the code to the preview, which compiles it on its next frame.
    void RunCheck()
    {
        CheckReport report = CompatibilityChecker.CheckSource(ImageBox.Text, CommonBox.Text, null);
        OutputBox.Text = report.ToString();

        if (!report.CanRun)
        {
            StatusText.Text = "Check failed: see the report below";
            return;
        }

        pendingCode = ShaderBuilder.Build(ImageBox.Text, CommonBox.Text);
        StatusText.Text = "Compiling...";
    }

    // Called by the GLWpfControl every frame, with its OpenGL context ready to use
    void Preview_OnRender(TimeSpan delta)
    {
        if (pendingCode != null)
        {
            CompilePreview(pendingCode);
            pendingCode = null;
        }

        // Size in real pixels (the window may be scaled, e.g. 125% on laptops)
        DpiScale dpi = VisualTreeHelper.GetDpi(Preview);
        int w = Math.Max(1, (int)(Preview.ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)(Preview.ActualHeight * dpi.DpiScaleY));

        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        if (!previewRunning || previewProgram == 0) return;   // paused: just a black box

        previewTime += (float)Math.Min(delta.TotalSeconds, 0.1);
        GL.UseProgram(previewProgram);
        GL.Uniform3(GL.GetUniformLocation(previewProgram, "iResolution"), (float)w, (float)h, 1f);
        GL.Uniform1(GL.GetUniformLocation(previewProgram, "iTime"), previewTime);
        GL.BindVertexArray(previewVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        infoTimer += delta.TotalSeconds;
        if (infoTimer >= 0.25)
        {
            PreviewInfo.Text = $"{previewTime:0.0} s   ·   {w}×{h}";
            infoTimer = 0;
        }
    }

    void CompilePreview(string code)
    {
        if (previewVao == 0) previewVao = GL.GenVertexArray();

        CompileResult result = ShaderBuilder.Compile(code);
        if (!result.Ok)
        {
            OutputBox.AppendText(CompatibilityChecker.ExplainCompileErrors(result.Log));
            StatusText.Text = "Check failed: see the report below";
            return;   // keep showing the last shader that worked
        }

        if (previewProgram != 0) GL.DeleteProgram(previewProgram);
        previewProgram = result.Program;
        previewTime = 0;
        previewRunning = true;
        OutputBox.AppendText("  [OK] Compiles natively (GLSL 4.30)\n");
        StatusText.Text = "Check passed: preview running";
    }

    void SetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        string? exe = FindEngine();
        if (exe == null) return;

        StopWallpaper();          // only one wallpaper at a time

        // Only the wallpaper should use the GPU now
        previewRunning = false;
        PreviewInfo.Text = "Preview paused: the shader is running as your wallpaper";
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

    // ---------- library ----------

    void SaveShader_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name == "") name = "Untitled " + (library.Count + 1);

        // Same name = update that shader; new name = add a new one
        SavedShader? existing = library.FirstOrDefault(s => s.Name == name);
        if (existing != null)
        {
            existing.Image = ImageBox.Text;
            existing.Common = CommonBox.Text;
            existing.Saved = DateTime.Now;
        }
        else
        {
            existing = new SavedShader { Name = name, Image = ImageBox.Text, Common = CommonBox.Text, Saved = DateTime.Now };
            library.Add(existing);
        }

        ShaderLibrary.Save(library);
        NameBox.Text = name;
        LibraryList.SelectedItem = existing;
        StatusText.Text = $"Saved \"{name}\"";
    }

    void DeleteShader_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not SavedShader selected)
        {
            StatusText.Text = "Pick a shader in the library to delete";
            return;
        }

        var answer = MessageBox.Show($"Delete \"{selected.Name}\" from the library?", "ShaderWall",
                                     MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        library.Remove(selected);
        ShaderLibrary.Save(library);
        StatusText.Text = $"Deleted \"{selected.Name}\"";
    }

    // Clicking a saved shader loads its code into the tabs
    void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LibraryList.SelectedItem is not SavedShader selected) return;

        ImageBox.Text = selected.Image;
        CommonBox.Text = selected.Common;
        NameBox.Text = selected.Name;
        OutputBox.Text = $"Loaded \"{selected.Name}\" (saved {selected.Saved:g}). Press Check + preview, or Set as wallpaper.";
    }

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
