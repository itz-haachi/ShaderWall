using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Text;

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

class ShaderWindow : GameWindow
{
    float time = 0;
    int program;
    int vao;
    int frames = 0;
    double fpsTimer = 0;
    string originalWallpaper = "";
    public volatile bool quitRequested = false;
    public ShaderWindow(GameWindowSettings g, NativeWindowSettings n) : base(g, n) { }
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
    }
    protected override void OnUnload()
    {
        // Put the user's normal wallpaper back (also refreshes the desktop)
        Win32.SetWallpaper(0x0014, 0, originalWallpaper, 0x01 | 0x02);
        Console.WriteLine("Wallpaper restored.");
        base.OnUnload();
    }
    bool IsWallpaperHidden()
{
    IntPtr front = Win32.GetForegroundWindow();
    if (front == IntPtr.Zero) return false;

    // If the desktop itself is in front, the wallpaper is visible
    var name = new StringBuilder(256);
    Win32.GetClassName(front, name, name.Capacity);
    string cls = name.ToString();
    if (cls == "Progman" || cls == "WorkerW") return false;

    // A maximized window hides the wallpaper
    if (Win32.IsZoomed(front)) return true;

    // A fullscreen window (games, videos) covers the whole screen
    Win32.GetWindowRect(front, out Win32.RECT r);
    int screenW = Win32.GetSystemMetrics(0);
    int screenH = Win32.GetSystemMetrics(1);
    return r.Left <= 0 && r.Top <= 0 && r.Right >= screenW && r.Bottom >= screenH;
}
    protected override void OnLoad()
    {
      base.OnLoad();

      string vertexCode = """
          #version 330 core
          void main()
          {
              vec2 pos[3] = vec2[](vec2(-1,-1), vec2(3,-1), vec2(-1,3));
              gl_Position = vec4(pos[gl_VertexID], 0, 1);
          }
          """;

      string userCode = File.ReadAllText("shader.glsl");
      string header = """
          #version 430 core
          out vec4 outColor;
          uniform vec3 iResolution;
          uniform float iTime;
          uniform float iTimeDelta;
          uniform int iFrame;
          uniform vec4 iMouse;
          """;

      string footer = """
          void main()
          {
              mainImage(outColor, gl_FragCoord.xy);
          }
          """;
      string commonCode = "";
      if(File.Exists("common.glsl")) {
        commonCode = File.ReadAllText("common.glsl");
      }
      string fragmentCode = header + "\n" + commonCode + "\n" + userCode + "\n" + footer;
      int vs = GL.CreateShader(ShaderType.VertexShader);
      GL.ShaderSource(vs, vertexCode);
      GL.CompileShader(vs);
      Console.WriteLine(GL.GetShaderInfoLog(vs));

      int fs = GL.CreateShader(ShaderType.FragmentShader);
      GL.ShaderSource(fs, fragmentCode);
      GL.CompileShader(fs);
      Console.WriteLine(GL.GetShaderInfoLog(fs));

      program = GL.CreateProgram();
      GL.AttachShader(program, vs);
      GL.AttachShader(program, fs);
      GL.LinkProgram(program);

      vao = GL.GenVertexArray();
      //Console.Write(fragmentCode);
      IntPtr progman = Win32.FindWindow("Progman", null);
      IntPtr myWindow = Win32.FindWindow(null, Title);
      IntPtr icons = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

      long exStyle = Win32.GetWindowLongPtr(progman, -20).ToInt64();
      bool raised = (exStyle & 0x00200000) != 0;

      Console.WriteLine("Progman: " + progman);
      Console.WriteLine("My window: " + myWindow);
      Console.WriteLine("Icons inside Progman: " + icons);
      Console.WriteLine("New 24H2 layout: " + raised);

      // Remember the current wallpaper so it can be restored on exit
      var wp = new StringBuilder(260);
      Win32.GetWallpaper(0x0073, (uint)wp.Capacity, wp, 0);
      originalWallpaper = wp.ToString();
      Console.WriteLine("Saved wallpaper: " + originalWallpaper);

      // ---- shared by both layouts ----
      // 1. Ask Progman to set up its wallpaper layer
      Win32.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

      // 2. Make my window a borderless child
      const long WS_CHILD = 0x40000000;
      const long WS_VISIBLE = 0x10000000;
      Win32.SetWindowLongPtr(myWindow, -16, new IntPtr(WS_CHILD | WS_VISIBLE));

      int screenW = Win32.GetSystemMetrics(0);
      int screenH = Win32.GetSystemMetrics(1);

      if (raised)
      {
          // ---- new layout (Windows 11 24H2+) ----
          // 3. Make it "layered" and fully opaque
          long myEx = Win32.GetWindowLongPtr(myWindow, -20).ToInt64();
          Win32.SetWindowLongPtr(myWindow, -20, new IntPtr(myEx | 0x00080000));
          Win32.SetLayeredWindowAttributes(myWindow, 0, 255, 0x2);

          // 4. Move it inside Progman, full screen, just below the icons
          Win32.SetParent(myWindow, progman);
          Win32.SetWindowPos(myWindow, icons, 0, 0, screenW, screenH, 0x0040);

          // 5. Push Windows' own wallpaper layer to the very back
          IntPtr workerW = Win32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
          Win32.SetWindowPos(workerW, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
          Console.WriteLine("Attached using the new layout.");
      }
      else
      {
          // ---- classic layout (Windows 10, Windows 11 up to 23H2) ----
          IntPtr workerW = IntPtr.Zero;

          // Visit every top-level window; find the one holding the icons,
          // then take the WorkerW that sits right behind it.
          Win32.EnumWindows((hWnd, lParam) =>
          {
              IntPtr shell = Win32.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
              if (shell != IntPtr.Zero)
              {
                  workerW = Win32.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
              }
              return true;
          }, IntPtr.Zero);

          if (workerW != IntPtr.Zero)
          {
              Win32.SetParent(myWindow, workerW);
              Win32.SetWindowPos(myWindow, IntPtr.Zero, 0, 0, screenW, screenH, 0x0040);
              Console.WriteLine("Attached using the classic layout.");
          }
          else
          {
              Console.WriteLine("Classic layout: couldn't find WorkerW.");
          }
      }

    }
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        
        //time += (float)args.Time;
        base.OnRenderFrame(args);

        if (quitRequested)
        {
            Close();
            return;
        }

        if (IsWallpaperHidden())
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
            Console.WriteLine("FPS: " + frames);
            frames = 0;
            fpsTimer = 0;
        }

        GL.ClearColor(0.5f + 0.5f * MathF.Sin(time), 0.2f, 0.8f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.UseProgram(program);
        GL.Uniform3(GL.GetUniformLocation(program, "iResolution"), (float)ClientSize.X, (float)ClientSize.Y, 1.0f);
        GL.Uniform1(GL.GetUniformLocation(program, "iTime"), time);
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        SwapBuffers();
    }
}
static class Win32
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int w, int h, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
}

[DllImport("user32.dll")]
public static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

[DllImport("user32.dll")]
public static extern bool IsZoomed(IntPtr hWnd);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[DllImport("user32.dll")]
public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
public static extern bool GetWallpaper(uint action, uint size, StringBuilder path, uint flags);

[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
public static extern bool SetWallpaper(uint action, uint param, string path, uint flags);

[DllImport("winmm.dll")]
public static extern uint timeBeginPeriod(uint milliseconds);

[DllImport("winmm.dll")]
public static extern uint timeEndPeriod(uint milliseconds);
}