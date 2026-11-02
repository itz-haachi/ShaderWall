using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Wpf;
using Forms = System.Windows.Forms;

namespace ShaderWallApp;

// One row of the compatibility checklist (icon + title + detail)
public class CheckRow
{
    public string Symbol { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public Brush CircleFill { get; init; } = Brushes.White;
    public Brush CircleBorder { get; init; } = Brushes.Black;
    public Brush SymbolColor { get; init; } = Brushes.Black;

    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x8C));

    // "OK" / "!" / "X" plus "Title: detail" from the CompatibilityChecker
    public static CheckRow From(string mark, string text)
    {
        int colon = text.IndexOf(':');
        string title = colon > 0 ? text[..colon] : text;
        string detail = colon > 0 ? text[(colon + 1)..].Trim() : "";
        return mark switch
        {
            "OK" => new CheckRow { Symbol = "✓", Title = title, Detail = detail, CircleFill = Ink, CircleBorder = Ink, SymbolColor = Brushes.White },
            "!" => new CheckRow { Symbol = "!", Title = title, Detail = detail, CircleFill = Brushes.White, CircleBorder = Grey, SymbolColor = Grey },
            _ => new CheckRow { Symbol = "✕", Title = title, Detail = detail, CircleFill = Brushes.White, CircleBorder = Ink, SymbolColor = Ink },
        };
    }
}

// The app window. It shows a live preview itself (GLWpfControl) and starts the
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
    float previewDelta;       // iTimeDelta for the preview
    int previewFrame;         // iFrame for the preview
    float previewFrameRate;   // iFrameRate for the preview (smoothed)
    bool previewRunning;    // true after Check + preview; false while the wallpaper runs
    bool previewPaused;     // the pause button on the preview
    double infoTimer;       // update the preview's text 4 times a second, not every frame
    int previewFrames;
    SavedShader? thumbnailFor;   // take a thumbnail of the next preview frame for this shader

    // Saved shaders. An ObservableCollection tells the Library grid to refresh itself when items change.
    readonly ObservableCollection<SavedShader> library = new(ShaderLibrary.Load());
    readonly ObservableCollection<CheckRow> checks = new();
    readonly ObservableCollection<string> tags = new();
    bool? lastMultiPass;    // only known for shaders fetched through the API
    bool ignoreSelection;   // true while we change the Library selection from code

    readonly AppSettings settings = AppSettings.Load();
    bool applyingSettings;  // true while we set controls from saved settings (so they don't save themselves back)

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

        LibraryList.ItemsSource = library;
        CollectionViewSource.GetDefaultView(library).Filter = MatchesSearch;
        library.CollectionChanged += (s, e) => UpdateLibraryCount();
        UpdateLibraryCount();

        ChecksList.ItemsSource = checks;
        TagsList.ItemsSource = tags;
        UpdateTags();

        FillDisplays();
        ApiKeyStatus.Text = System.IO.File.Exists(ShaderLibrary.ApiKeyPath)
            ? "Key saved. Fetching by link is ready."
            : "No key yet. Keys need a Silver or Gold Shadertoy account.";
        LibraryPathText.Text = "Library file: " + ShaderLibrary.FilePath;
        VersionText.Text = "Version " + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?");

        // Put the saved settings into the controls
        applyingSettings = true;
        Fps15.IsChecked = settings.FpsCap == 15;
        Fps30.IsChecked = settings.FpsCap == 30;
        Fps60.IsChecked = settings.FpsCap == 60;
        FpsMax.IsChecked = settings.FpsCap == 0;
        StartupSwitch.IsChecked = StartupManager.IsEnabled();
        ScaleSlider.Value = settings.RenderScale;
        ScaleText.Text = settings.RenderScale + "%";
        SpeedSlider.Value = settings.Speed;
        SpeedText.Text = SpeedLabel(settings.Speed);
        ApplySlideshowSettingsToControls();
        applyingSettings = false;

        ShowPage(AddPage);
        SetupTray();                // MainWindow.Tray.cs
        SetupSlideshow();           // MainWindow.Slideshow.cs
        Closing += MainWindow_Closing;
    }

    // ================= navigation =================

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (AddPage == null) return;   // still loading: the pages don't exist yet
        if (sender == NavAdd) ShowPage(AddPage);
        else if (sender == NavLibrary) ShowPage(LibraryPage);
        else if (sender == NavDisplays) ShowPage(DisplaysPage);
        else if (sender == NavSettings) ShowPage(SettingsPage);
        else ShowPage(AboutPage);
    }

    void ShowPage(FrameworkElement page)
    {
        foreach (FrameworkElement p in new FrameworkElement[] { AddPage, LibraryPage, DisplaysPage, SettingsPage, AboutPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
    }

    void GoToAdd_Click(object sender, RoutedEventArgs e)
    {
        NavAdd.IsChecked = true;
        SegPaste.IsChecked = true;
    }

    // ================= Add shader: where the code comes from =================

    void Source_Checked(object sender, RoutedEventArgs e)
    {
        if (UrlPanel == null) return;   // still loading
        UrlPanel.Visibility = SegUrl.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PastePanel.Visibility = SegPaste.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        FilePanel.Visibility = SegFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void EditorTab_Checked(object sender, RoutedEventArgs e)
    {
        if (ImageBox == null) return;   // still loading
        ImageBox.Visibility = EdImage.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CommonBox.Visibility = EdCommon.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void PasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        string text = Clipboard.GetText();
        if (EdCommon.IsChecked == true) CommonBox.Text = text;
        else ImageBox.Text = text;
        MetaText.Text = "Pasted code";
        lastMultiPass = null;
    }

    void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a shader",
            Filter = "Shader files (*.glsl;*.frag;*.txt)|*.glsl;*.frag;*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        ImageBox.Text = System.IO.File.ReadAllText(dialog.FileName);
        string commonPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dialog.FileName)!, "common.glsl");
        CommonBox.Text = System.IO.File.Exists(commonPath) && commonPath != dialog.FileName
            ? System.IO.File.ReadAllText(commonPath) : "";

        NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        MetaText.Text = "From file";
        FilePathText.Text = dialog.FileName;
        lastMultiPass = null;
        RunCheck();
    }

    async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        string input = UrlBox.Text.Trim();
        ShaderSource? src;

        if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(input))
        {
            src = ShaderLoader.FromJsonFile(input);   // test mode: a saved API reply
        }
        else
        {
            if (!System.IO.File.Exists(ShaderLibrary.ApiKeyPath))
            {
                ShowProblem("Needs an API key: add your Shadertoy key in Settings (Silver or Gold account), or use Paste code.");
                return;
            }
            FetchButton.IsEnabled = false;
            src = await ShaderLoader.FromUrlAsync(input, System.IO.File.ReadAllText(ShaderLibrary.ApiKeyPath).Trim());
            FetchButton.IsEnabled = true;
        }

        if (src == null)
        {
            ShowProblem("Couldn't fetch: " + ShaderLoader.LastMessage);
            return;
        }

        ImageBox.Text = src.ImageCode;
        CommonBox.Text = src.CommonCode;
        NameBox.Text = src.Name;
        MetaText.Text = $"by {src.Author} · fetched via Shadertoy API";
        lastMultiPass = src.MultiPass;
        RunCheck();
    }

    void ShowProblem(string text)
    {
        checks.Clear();
        checks.Add(CheckRow.From("X", text));
        ErrorBox.Visibility = Visibility.Collapsed;
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (NameHint != null) NameHint.Visibility = NameBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= check + live preview =================

    void Check_Click(object sender, RoutedEventArgs e) => RunCheck();

    // Step 1 (no GPU): read the code and report problems.
    // Step 2 (GPU): hand the code to the preview, which compiles it on its next frame.
    void RunCheck()
    {
        CheckReport report = CompatibilityChecker.CheckSource(ImageBox.Text, CommonBox.Text, lastMultiPass);
        checks.Clear();
        foreach (var (mark, text) in report.Items)
            checks.Add(CheckRow.From(mark, text));
        ErrorBox.Visibility = Visibility.Collapsed;
        UpdateTags();

        if (!report.CanRun)
        {
            EditorStatus.Text = "✕ Can't run: see the compatibility check";
            return;
        }

        pendingCode = ShaderBuilder.Build(ImageBox.Text, CommonBox.Text);
        EditorStatus.Text = "Compiling...";
    }

    void UpdateTags()
    {
        tags.Clear();
        tags.Add("Image");
        if (CommonBox.Text.Trim() != "") tags.Add("Common");
        if (lastMultiPass == true) tags.Add("Multi-pass");
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
        if (!previewRunning || previewProgram == 0) return;   // nothing to show: a black box

        previewDelta = previewPaused ? 0f : (float)Math.Min(delta.TotalSeconds, 0.1) * settings.Speed / 100f;
        previewTime += previewDelta;
        float instant = delta.TotalSeconds > 0 ? (float)(1.0 / delta.TotalSeconds) : 0f;
        previewFrameRate = previewFrameRate == 0 ? instant : previewFrameRate * 0.9f + instant * 0.1f;

        GL.UseProgram(previewProgram);
        ShaderBuilder.SetInputs(previewProgram, w, h, previewTime, previewDelta, previewFrame, previewFrameRate);
        GL.BindVertexArray(previewVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        if (!previewPaused) previewFrame++;

        if (thumbnailFor != null)
        {
            SaveThumbnail(thumbnailFor, w, h);
            thumbnailFor = null;
        }

        previewFrames++;
        infoTimer += delta.TotalSeconds;
        if (infoTimer >= 0.25)
        {
            PreviewTimeText.Text = TimeSpan.FromSeconds(previewTime).ToString(@"mm\:ss\.f");
            PreviewResText.Text = $"{w}×{h}";
            PreviewFpsText.Text = previewPaused ? "paused" : $"{Math.Round(previewFrames / infoTimer)} fps";
            TimeFill.Width = TimeTrack.ActualWidth * (previewTime % 10f) / 10f;   // the bar loops every 10 s
            infoTimer = 0;
            previewFrames = 0;
        }
    }

    void CompilePreview(string code)
    {
        if (previewVao == 0) previewVao = GL.GenVertexArray();

        CompileResult result = ShaderBuilder.Compile(code);
        if (!result.Ok)
        {
            checks.Add(CheckRow.From("X", "Doesn't compile: the errors are shown below"));
            ErrorBox.Text = CompatibilityChecker.ExplainCompileErrors(result.Log).Trim();
            ErrorBox.Visibility = Visibility.Visible;
            EditorStatus.Text = "✕ Didn't compile: the preview keeps the last shader that worked";
            return;
        }

        if (previewProgram != 0) GL.DeleteProgram(previewProgram);
        previewProgram = result.Program;
        previewTime = 0;
        previewFrame = 0;
        previewRunning = true;
        previewPaused = false;
        PreviewPauseIcon.Text = "";
        PreviewBadgeText.Text = "Live preview · OpenGL";
        checks.Add(CheckRow.From("OK", "Compiles natively: GLSL 4.30"));
        EditorStatus.Text = "✓ Compiled · GLSL 4.30";
    }

    void PreviewPause_Click(object sender, RoutedEventArgs e)
    {
        previewPaused = !previewPaused;
        PreviewPauseIcon.Text = previewPaused ? "" : "";   // play / pause icons
    }

    // Read the preview's pixels and save them as a PNG for the Library card
    void SaveThumbnail(SavedShader shader, int w, int h)
    {
        var pixels = new byte[w * h * 4];
        GL.ReadPixels(0, 0, w, h, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // OpenGL counts rows from the bottom; images count from the top, so flip it
        int row = w * 4;
        var flipped = new byte[pixels.Length];
        for (int y = 0; y < h; y++)
            System.Buffer.BlockCopy(pixels, y * row, flipped, (h - 1 - y) * row, row);   // System.Buffer, not OpenTK's GL Buffer
        for (int i = 3; i < flipped.Length; i += 4) flipped[i] = 255;   // ignore the shader's alpha

        BitmapSource bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, flipped, row);
        System.IO.Directory.CreateDirectory(ShaderLibrary.ThumbFolder);
        string path = System.IO.Path.Combine(ShaderLibrary.ThumbFolder, Guid.NewGuid().ToString("N") + ".png");
        using (var file = System.IO.File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        }

        string? old = shader.ThumbnailPath;
        shader.ThumbnailPath = path;
        ShaderLibrary.Save(library);
        if (old != null)
        {
            try { System.IO.File.Delete(old); } catch (System.IO.IOException) { }
        }
    }

    // ================= wallpaper =================

    void SetWallpaper_Click(object sender, RoutedEventArgs e) => StartWallpaper();

    // Runs whatever is in the Image / Common boxes as the wallpaper
    void StartWallpaper()
    {
        // Already running? Swap the shader inside the running engine: no restart, no gap.
        if (engine != null && !engine.HasExited)
        {
            SwitchRunningShader();
            return;
        }

        string? exe = FindEngine();
        if (exe == null) return;

        // Only the wallpaper should use the GPU now
        previewRunning = false;
        PreviewBadgeText.Text = "Paused · running as your wallpaper";
        WriteShaderFiles();
        OutputBox.Text = "";

        var info = new ProcessStartInfo(exe, $"--listen {SelectedFps()} --scale={settings.RenderScale} --speed={settings.Speed}")
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,    // so we can send "quit", "pause", "resume"
            RedirectStandardOutput = true,   // so we can show what it prints
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        engine = new Process { StartInfo = info, EnableRaisingEvents = true };
        engine.OutputDataReceived += (s, args) =>
        {
            if (args.Data == null) return;
            Dispatcher.Invoke(() =>
            {
                OutputBox.AppendText(args.Data + "\n");

                // The new shader didn't compile: the engine kept the old one, so we go back to its name
                if (args.Data.StartsWith("Reload failed") && previousName != null)
                {
                    EditorStatus.Text = $"Couldn't switch to \"{runningName}\": it didn't compile. Still showing \"{previousName}\".";
                    runningName = previousName;
                    previousName = null;
                    UpdateTray();
                }
                else if (args.Data == "Reloaded.")
                {
                    previousName = null;
                }
            });
        };
        engine.Exited += (s, args) => Dispatcher.Invoke(() =>
        {
            wallpaperPaused = false;
            UpdateTray();
        });

        engine.Start();
        engine.BeginOutputReadLine();
        wallpaperPaused = false;
        runningName = NameBox.Text.Trim() == "" ? "Unsaved shader" : NameBox.Text.Trim();
        EditorStatus.Text = $"\"{runningName}\" is now your wallpaper";
        UpdateTray();

        // Remember it, so it can come back when Windows starts
        settings.WallpaperOn = true;
        settings.LastName = runningName;
        settings.LastImage = ImageBox.Text;
        settings.LastCommon = CommonBox.Text;
        settings.Save();
    }

    string? previousName;   // the shader that was showing before a switch (in case the switch fails)

    void SwitchRunningShader()
    {
        previousName = runningName;
        WriteShaderFiles();
        engine!.StandardInput.WriteLine("reload");

        previewRunning = false;
        PreviewBadgeText.Text = "Paused · running as your wallpaper";
        wallpaperPaused = false;
        runningName = NameBox.Text.Trim() == "" ? "Unsaved shader" : NameBox.Text.Trim();
        EditorStatus.Text = $"\"{runningName}\" is now your wallpaper";
        UpdateTray();

        settings.WallpaperOn = true;
        settings.LastName = runningName;
        settings.LastImage = ImageBox.Text;
        settings.LastCommon = CommonBox.Text;
        settings.Save();
    }

    // Stop chosen by the user (card or tray): also forget it for next log-in.
    // Quitting the app does NOT do this, so the wallpaper comes back next time.
    void StopByUser()
    {
        StopWallpaper();
        settings.WallpaperOn = false;
        settings.Save();
        UpdateTray();
    }

    // Started by Windows with --tray: no window, just the tray icon and the last wallpaper
    public async void StartInTray()
    {
        if (!settings.WallpaperOn || settings.LastImage.Trim() == "") return;

        // Right after log-in the desktop may not be ready yet, so give Explorer a moment
        await Task.Delay(4000);

        ImageBox.Text = settings.LastImage;
        CommonBox.Text = settings.LastCommon;
        NameBox.Text = settings.LastName;
        StartWallpaper();
    }

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

    void CardPause_Click(object sender, RoutedEventArgs e) => TogglePause();

    void CardStop_Click(object sender, RoutedEventArgs e) => StopByUser();

    // The black status card in the sidebar. Called from UpdateTray, so card and tray always agree.
    void UpdateStatusCard()
    {
        bool running = engine != null && !engine.HasExited;
        CardTitle.Text = !running ? "Not running" : wallpaperPaused ? "Wallpaper paused" : "Wallpaper running";
        CardDot.Opacity = running && !wallpaperPaused ? 1 : 0.35;
        CardShader.Text = running ? runningName : "Pick a shader to start";
        CardInfo.Text = (SelectedFps() == 0 ? "no FPS cap" : $"{SelectedFps()} fps")
                        + (settings.RenderScale < 100 ? $" · {settings.RenderScale}%" : "")
                        + (settings.Speed != 100 ? $" · {SpeedLabel(settings.Speed)}" : "");
        CardPause.Content = wallpaperPaused ? "Resume" : "Pause";
        CardActions.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        foreach (SavedShader s in library)
            s.IsActive = running && s.Name == runningName;
    }

    // ================= library =================

    void SaveShader_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name == "")
        {
            name = "Untitled " + (library.Count + 1);
            NameBox.Text = name;
        }

        // Same name = update that shader; new name = add a new one
        SavedShader? shader = library.FirstOrDefault(s => s.Name == name);
        if (shader == null)
        {
            shader = new SavedShader { Name = name };
            library.Add(shader);
        }
        shader.Image = ImageBox.Text;
        shader.Common = CommonBox.Text;
        shader.Saved = DateTime.Now;
        ShaderLibrary.Save(library);

        // A thumbnail needs a running preview: grab its next frame
        if (previewRunning && previewProgram != 0)
        {
            thumbnailFor = shader;
            EditorStatus.Text = $"Saved \"{name}\" to your library";
        }
        else
        {
            EditorStatus.Text = $"Saved \"{name}\". Preview it and save again to get a thumbnail.";
        }
    }

    // Clicking a card makes it the wallpaper
    void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ignoreSelection || LibraryList.SelectedItem is not SavedShader shader) return;
        SetAsWallpaper(shader);

        // Clear the selection so the same card can be clicked again later
        ignoreSelection = true;
        LibraryList.SelectedItem = null;
        ignoreSelection = false;
    }

    void SetAsWallpaper(SavedShader shader)
    {
        LoadIntoEditor(shader);
        StartWallpaper();
    }

    void LoadIntoEditor(SavedShader shader)
    {
        ImageBox.Text = shader.Image;
        CommonBox.Text = shader.Common;
        NameBox.Text = shader.Name;
        MetaText.Text = "From your library · " + shader.SavedText;
        lastMultiPass = null;
        checks.Clear();
        UpdateTags();
    }

    // The "•••" button on a card
    void CardMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not SavedShader shader) return;

        var menu = new ContextMenu { PlacementTarget = button };
        menu.Items.Add(MenuItemFor("Set as wallpaper", () => SetAsWallpaper(shader)));
        menu.Items.Add(MenuItemFor("Edit and preview", () =>
        {
            LoadIntoEditor(shader);
            SegPaste.IsChecked = true;
            NavAdd.IsChecked = true;
            RunCheck();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Delete", () => DeleteShader(shader)));
        menu.IsOpen = true;
    }

    static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => action();
        return item;
    }

    void DeleteShader(SavedShader shader)
    {
        var answer = MessageBox.Show($"Delete \"{shader.Name}\" from the library?", "ShaderWall",
                                     MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        library.Remove(shader);
        ShaderLibrary.Save(library);
        if (shader.ThumbnailPath != null)
        {
            try { System.IO.File.Delete(shader.ThumbnailPath); } catch (System.IO.IOException) { }
        }
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        RefreshLibraryView();
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (LibraryList == null) return;   // still loading
        RefreshLibraryView();
    }

    // The star on a card
    void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SavedShader shader }) return;
        shader.Favourite = !shader.Favourite;
        ShaderLibrary.Save(library);
        RefreshLibraryView();   // e.g. un-starring while "Favourites" is selected hides the card
    }

    // Re-run the filter over every card, then update the "nothing here" message
    void RefreshLibraryView()
    {
        CollectionViewSource.GetDefaultView(library).Refresh();
        UpdateEmptyText();
    }

    // The search box AND the selected chip both have to match
    bool MatchesSearch(object item)
    {
        var shader = (SavedShader)item;
        if (FilterFav?.IsChecked == true && !shader.Favourite) return false;
        if (FilterAttention?.IsChecked == true && !shader.NeedsAttention) return false;

        string text = SearchBox?.Text.Trim() ?? "";
        return text == "" || shader.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    void UpdateEmptyText()
    {
        bool nothingShown = CollectionViewSource.GetDefaultView(library).IsEmpty;
        LibraryEmptyText.Text =
            library.Count == 0 ? "No shaders saved yet. Add one, preview it, then press Save to library." :
            FilterFav.IsChecked == true && SearchBox.Text.Trim() == "" ? "No favourites yet. Click the star on a card to add one." :
            FilterAttention.IsChecked == true && SearchBox.Text.Trim() == "" ? "Nothing needs attention: every shader passes the check." :
            "No shaders match your search.";
        LibraryEmptyText.Visibility = nothingShown ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateLibraryCount()
    {
        int n = library.Count;
        LibraryCountText.Text = (n == 1 ? "1 shader saved" : $"{n} shaders saved") + " · click one to make it your wallpaper";
        UpdateEmptyText();
    }

    // ================= displays + settings =================

    void FillDisplays()
    {
        DisplaysList.Children.Clear();
        SettingsDisplays.Children.Clear();
        int number = 1;
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            string size = $"{screen.Bounds.Width}×{screen.Bounds.Height}";
            DisplaysList.Children.Add(DisplayCard($"Display {number}", size,
                screen.Primary ? "Primary · the wallpaper runs here" : "", screen.Primary));
            SettingsDisplays.Children.Add(DisplayCard($"Display {number}", size,
                screen.Primary ? "Primary" : "", screen.Primary));
            number++;
        }
    }

    static Border DisplayCard(string title, string size, string note, bool primary)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 22,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = note == "" ? size : size + "  ·  " + note,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
            TextWrapping = TextWrapping.Wrap,
        });
        return new Border
        {
            Child = panel,
            BorderThickness = new Thickness(primary ? 1.5 : 1),
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(0x11, 0x11, 0x11) : Color.FromRgb(0xE2, 0xE2, 0xE2)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
        };
    }

    int SelectedFps() =>
        Fps15.IsChecked == true ? 15 :
        Fps60.IsChecked == true ? 60 :
        FpsMax.IsChecked == true ? 0 : 30;   // 0 = unlimited

    void Fps_Checked(object sender, RoutedEventArgs e)
    {
        if (applyingSettings || FpsMax == null) return;   // loading, or the controls aren't all created yet
        settings.FpsCap = SelectedFps();
        settings.Save();
        UpdateTray();   // the status card shows the cap
    }

    void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleText == null) return;   // still loading
        int percent = (int)Math.Round(ScaleSlider.Value);
        ScaleText.Text = percent + "%";
        if (applyingSettings) return;

        settings.RenderScale = percent;
        settings.Save();

        // Change the running wallpaper immediately
        if (engine != null && !engine.HasExited)
            engine.StandardInput.WriteLine("scale " + percent);
        UpdateTray();
    }

    void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedText == null) return;   // still loading
        int percent = (int)Math.Round(SpeedSlider.Value);
        SpeedText.Text = SpeedLabel(percent);
        if (applyingSettings) return;

        settings.Speed = percent;   // the preview reads this every frame
        settings.Save();

        // Change the running wallpaper immediately
        if (engine != null && !engine.HasExited)
            engine.StandardInput.WriteLine("speed " + percent);
        UpdateTray();
    }

    // 100 -> "1×", 150 -> "1.5×", 25 -> "0.25×"
    static string SpeedLabel(int percent) => (percent / 100.0).ToString("0.##") + "×";

    void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (applyingSettings) return;
        if (StartupSwitch.IsChecked == true) StartupManager.Enable();
        else StartupManager.Disable();
    }

    void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Password.Trim();
        if (key == "")
        {
            ApiKeyStatus.Text = "Type or paste your key first.";
            return;
        }
        System.IO.Directory.CreateDirectory(ShaderLibrary.Folder);
        System.IO.File.WriteAllText(ShaderLibrary.ApiKeyPath, key);
        ApiKeyBox.Clear();
        ApiKeyStatus.Text = "Key saved. Fetching by link is ready.";
    }

    // ================= helpers =================

    // The engine reads shader.glsl / common.glsl from its working folder
    void WriteShaderFiles()
    {
        System.IO.Directory.CreateDirectory(workDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(workDir, "shader.glsl"), ImageBox.Text);
        System.IO.File.WriteAllText(System.IO.Path.Combine(workDir, "common.glsl"), CommonBox.Text);
    }

    // The engine lives in the engine\ folder next to the app (the build copies it there)
    string? FindEngine()
    {
        string exe = System.IO.Path.Combine(AppContext.BaseDirectory, "engine", "ShaderWallEngine.exe");
        if (System.IO.File.Exists(exe)) return exe;

        EditorStatus.Text = "Couldn't find the engine (engine\\ShaderWallEngine.exe). Rebuild the solution.";
        return null;
    }
}
