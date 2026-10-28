// Moves a window behind the desktop icons, for both Windows desktop layouts.
static class DesktopAttacher
{
    const long WS_CHILD = 0x40000000;
    const long WS_VISIBLE = 0x10000000;

    public static void Attach(IntPtr myWindow)
    {
        IntPtr progman = Win32.FindWindow("Progman", null);
        IntPtr icons = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        long exStyle = Win32.GetWindowLongPtr(progman, -20).ToInt64();
        bool raised = (exStyle & 0x00200000) != 0;
        Console.WriteLine("New 24H2 layout: " + raised);

        // ---- shared by both layouts ----
        // Ask Progman to set up its wallpaper layer
        Win32.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        // Make my window a borderless child
        Win32.SetWindowLongPtr(myWindow, -16, new IntPtr(WS_CHILD | WS_VISIBLE));

        int screenW = Win32.GetSystemMetrics(0);
        int screenH = Win32.GetSystemMetrics(1);

        if (raised)
            AttachNewLayout(myWindow, progman, icons, screenW, screenH);
        else
            AttachClassicLayout(myWindow, screenW, screenH);
    }

    // Windows 11 24H2 and later
    static void AttachNewLayout(IntPtr myWindow, IntPtr progman, IntPtr icons, int screenW, int screenH)
    {
        // Make it "layered" and fully opaque
        long myEx = Win32.GetWindowLongPtr(myWindow, -20).ToInt64();
        Win32.SetWindowLongPtr(myWindow, -20, new IntPtr(myEx | 0x00080000));
        Win32.SetLayeredWindowAttributes(myWindow, 0, 255, 0x2);

        // Move it inside Progman, full screen, just below the icons
        Win32.SetParent(myWindow, progman);
        Win32.SetWindowPos(myWindow, icons, 0, 0, screenW, screenH, 0x0040);

        // Push Windows' own wallpaper layer to the very back
        IntPtr workerW = Win32.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        Win32.SetWindowPos(workerW, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        Console.WriteLine("Attached using the new layout.");
    }

    // Windows 10 and Windows 11 up to 23H2
    static void AttachClassicLayout(IntPtr myWindow, int screenW, int screenH)
    {
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
