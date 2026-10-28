using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL4;

// The window: sets everything up once, then draws the shader every frame.
class ShaderWindow : GameWindow
{
    float time = 0;
    int program;
    int vao;
    int frames = 0;
    double fpsTimer = 0;
    public volatile bool quitRequested = false;
    public bool showFps = false;   // turned on with: dotnet run -- --show-fps

    readonly string fragmentCode;

    public ShaderWindow(GameWindowSettings g, NativeWindowSettings n, string fragmentCode) : base(g, n)
    {
        this.fragmentCode = fragmentCode;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        // 1. Compile the shader code we were given
        program = ShaderBuilder.Compile(fragmentCode);
        vao = GL.GenVertexArray();

        // 2. Remember the user's wallpaper, then move this window behind the icons
        WallpaperBackup.Save();
        IntPtr myWindow = Win32.FindWindow(null, Title);
        DesktopAttacher.Attach(myWindow);
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        if (quitRequested)
        {
            Close();
            return;
        }

        if (PowerManager.IsWallpaperHidden())
        {
            Thread.Sleep(500);
            return;
        }

        time += MathF.Min((float)args.Time, 0.1f);

        // Count frames and print the FPS once per second
        frames++;
        fpsTimer += args.Time;
        if (fpsTimer >= 1.0)
        {
            if (showFps) Console.WriteLine("FPS: " + frames);
            frames = 0;
            fpsTimer = 0;
        }

        GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(program);
        GL.Uniform3(GL.GetUniformLocation(program, "iResolution"), (float)ClientSize.X, (float)ClientSize.Y, 1.0f);
        GL.Uniform1(GL.GetUniformLocation(program, "iTime"), time);
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        SwapBuffers();
    }

    protected override void OnUnload()
    {
        WallpaperBackup.Restore();
        base.OnUnload();
    }
}
