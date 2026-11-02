using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL4;

// The window: sets everything up once, then draws the shader every frame.
class ShaderWindow : GameWindow
{
    float time = 0;
    float timeDelta = 0;   // iTimeDelta
    int frame = 0;         // iFrame
    float frameRate = 0;   // iFrameRate (smoothed)
    int program;
    int vao;
    int frames = 0;
    double fpsTimer = 0;
    public volatile bool quitRequested = false;
    public volatile bool paused = false;   // set from the tray menu: keep the last frame, draw nothing new
    public volatile bool reloadRequested = false;   // "reload": compile shader.glsl again and swap it in
    public volatile float speed = 1f;        // 0.25-3: how fast iTime moves (1 = normal)
    public volatile float renderScale = 1f; // 0.1-1: draw at this fraction of the screen size, then stretch

    // The smaller hidden image we draw into when renderScale < 1 (a framebuffer with a texture attached)
    int fbo, fboTexture, fboWidth, fboHeight;
    public bool showFps = false;   // turned on with: dotnet run -- --show-fps
    public bool checkOnly = false; // --check-only: compile, report, exit

    readonly string fragmentCode;
    IntPtr myWindow;   // our own window handle, set when we attach to the desktop

    public ShaderWindow(GameWindowSettings g, NativeWindowSettings n, string fragmentCode) : base(g, n)
    {
        this.fragmentCode = fragmentCode;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        // 1. Compile the shader code we were given. If it fails, stop before touching the desktop.
        CompileResult result = ShaderBuilder.Compile(fragmentCode);
        if (!result.Ok)
        {
            CompatibilityChecker.PrintCompileErrors(result.Log);
            Console.WriteLine("The shader didn't compile, so your wallpaper was not changed.");
            Environment.ExitCode = 1;
            Close();
            return;
        }
        Console.WriteLine("  [OK] Compiles natively (GLSL 4.30)");
        if (checkOnly)
        {
            Close();
            return;
        }
        program = result.Program;
        vao = GL.GenVertexArray();

        // 2. Remember the user's wallpaper, then move this window behind the icons
        WallpaperBackup.Save();
        myWindow = Win32.FindWindow(null, Title);
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

        // GL calls must happen on this (render) thread, so the listener only sets a flag
        if (reloadRequested)
        {
            reloadRequested = false;
            ReloadShader();
        }

        if (paused)
        {
            Thread.Sleep(100);
            return;
        }

        if (PowerManager.IsWallpaperHidden(myWindow))
        {
            Thread.Sleep(500);
            return;
        }

        float dt = MathF.Min((float)args.Time, 0.1f) * speed;   // speed stretches or squeezes the animation clock
        time += dt;
        timeDelta = dt;

        // Frame rate, smoothed so it doesn't jump around from frame to frame
        float instant = args.Time > 0 ? (float)(1.0 / args.Time) : 0f;
        frameRate = frameRate == 0 ? instant : frameRate * 0.9f + instant * 0.1f;

        // Count frames and print the FPS once per second
        frames++;
        fpsTimer += args.Time;
        if (fpsTimer >= 1.0)
        {
            if (showFps) Console.WriteLine("FPS: " + frames);
            frames = 0;
            fpsTimer = 0;
        }

        int screenW = ClientSize.X, screenH = ClientSize.Y;
        float scale = renderScale;

        if (scale >= 0.999f)
        {
            // Full resolution: draw straight to the window
            DrawShader(screenW, screenH);
        }
        else
        {
            // 1. Draw into the smaller hidden image
            int w = Math.Max(1, (int)(screenW * scale));
            int h = Math.Max(1, (int)(screenH * scale));
            EnsureFramebuffer(w, h);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            DrawShader(w, h);

            // 2. Stretch it onto the window (Linear = smooth instead of blocky)
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);   // 0 = the window itself
            GL.BlitFramebuffer(0, 0, w, h, 0, 0, screenW, screenH,
                               ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        frame++;
        SwapBuffers();
    }

    // Hot-swap: build the new shader; only replace the old one if the new one compiles.
    // The wallpaper never disappears, so there's no flash of the normal wallpaper.
    void ReloadShader()
    {
        string image = File.Exists("shader.glsl") ? File.ReadAllText("shader.glsl") : "";
        string common = File.Exists("common.glsl") ? File.ReadAllText("common.glsl") : "";

        CheckReport report = CompatibilityChecker.CheckSource(image, common, null);
        report.Print();
        if (!report.CanRun)
        {
            Console.WriteLine("Reload failed: keeping the current shader.");
            return;
        }

        CompileResult result = ShaderBuilder.Compile(ShaderBuilder.Build(image, common));
        if (!result.Ok)
        {
            CompatibilityChecker.PrintCompileErrors(result.Log);
            Console.WriteLine("Reload failed: keeping the current shader.");
            return;
        }

        GL.DeleteProgram(program);   // free the old shader on the GPU
        program = result.Program;
        time = 0;
        frame = 0;
        paused = false;
        Console.WriteLine("  [OK] Compiles natively (GLSL 4.30)");
        Console.WriteLine("Reloaded.");
    }

    // Run the shader over a w x h area of whatever framebuffer is bound
    void DrawShader(int w, int h)
    {
        GL.Viewport(0, 0, w, h);
        GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(program);
        ShaderBuilder.SetInputs(program, w, h, time, timeDelta, frame, frameRate);   // w x h: the shader sees the smaller size
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    // (Re)create the hidden image when its size changes
    void EnsureFramebuffer(int w, int h)
    {
        if (fbo != 0 && w == fboWidth && h == fboHeight) return;
        DeleteFramebuffer();

        fboTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, fboTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0,
                      PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);   // empty image, no starting pixels

        fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                TextureTarget.Texture2D, fboTexture, 0);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        fboWidth = w;
        fboHeight = h;
    }

    void DeleteFramebuffer()
    {
        if (fbo != 0) GL.DeleteFramebuffer(fbo);
        if (fboTexture != 0) GL.DeleteTexture(fboTexture);
        fbo = fboTexture = 0;
    }

    protected override void OnUnload()
    {
        DeleteFramebuffer();
        WallpaperBackup.Restore();
        base.OnUnload();
    }
}
