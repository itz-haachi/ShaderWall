using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;   // short name for Windows Forms, so it can't be confused with WPF
using Drawing = System.Drawing;

namespace ShaderWallApp;

// The tray icon part of MainWindow. "partial" means this is the same class as
// MainWindow.xaml.cs, just split across two files to keep each one readable.
public partial class MainWindow
{
    Forms.NotifyIcon tray = null!;
    Forms.ToolStripMenuItem pauseItem = null!, stopItem = null!, switchItem = null!, nowItem = null!;

    bool wallpaperPaused;
    string runningName = "";
    bool reallyQuit;          // true only when the user picks "Quit" (closing the window just hides it)
    bool shownTrayHint;       // show the "still running" bubble only once

    void SetupTray()
    {
        nowItem = new Forms.ToolStripMenuItem("Not running") { Enabled = false };
        pauseItem = new Forms.ToolStripMenuItem("Pause wallpaper", null, (s, e) => TogglePause());
        switchItem = new Forms.ToolStripMenuItem("Switch shader");
        stopItem = new Forms.ToolStripMenuItem("Stop wallpaper", null, (s, e) => { StopWallpaper(); UpdateTray(); });

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(nowItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(pauseItem);
        menu.Items.Add(switchItem);
        menu.Items.Add(stopItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open ShaderWall", null, (s, e) => ShowFromTray());
        menu.Items.Add("Quit and restore wallpaper", null, (s, e) => QuitApp());
        menu.Opening += (s, e) => FillSwitchMenu();   // rebuild the shader list each time, in case the library changed

        tray = new Forms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "ShaderWall",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (s, e) => ShowFromTray();
        UpdateTray();
    }

    // Keep the menu in step with what the wallpaper is doing
    void UpdateTray()
    {
        if (reallyQuit) return;   // the tray icon is being removed
        bool running = engine != null && !engine.HasExited;
        nowItem.Text = running ? $"Running: {runningName}" + (wallpaperPaused ? " (paused)" : "") : "Not running";
        pauseItem.Text = wallpaperPaused ? "Resume wallpaper" : "Pause wallpaper";
        pauseItem.Enabled = running;
        stopItem.Enabled = running;
        tray.Text = running ? "ShaderWall: " + Shorten(runningName, 40) : "ShaderWall";   // tooltips max out at 63 chars
    }

    void TogglePause()
    {
        if (engine == null || engine.HasExited) return;
        wallpaperPaused = !wallpaperPaused;
        engine.StandardInput.WriteLine(wallpaperPaused ? "pause" : "resume");
        UpdateTray();
    }

    void FillSwitchMenu()
    {
        switchItem.DropDownItems.Clear();
        if (library.Count == 0)
        {
            switchItem.DropDownItems.Add(new Forms.ToolStripMenuItem("(library is empty)") { Enabled = false });
            return;
        }
        foreach (SavedShader shader in library)
        {
            SavedShader chosen = shader;   // a fresh variable for each lambda to remember
            switchItem.DropDownItems.Add(shader.Name, null, (s, e) =>
            {
                LibraryList.SelectedItem = chosen;   // loads its code into the tabs
                StartWallpaper();
            });
        }
    }

    void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    void QuitApp()
    {
        reallyQuit = true;
        Close();
    }

    // The X button hides the window instead of quitting, so the wallpaper keeps running
    void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!reallyQuit)
        {
            e.Cancel = true;
            Hide();
            if (!shownTrayHint)
            {
                tray.ShowBalloonTip(3000, "ShaderWall is still running",
                    "Right-click the tray icon to pause, switch shader or quit.", Forms.ToolTipIcon.Info);
                shownTrayHint = true;
            }
            return;
        }

        StopWallpaper();        // restores the normal wallpaper
        tray.Visible = false;   // otherwise a ghost icon stays until you hover over it
        tray.Dispose();
    }

    // A small black square with a white ring, drawn in code (no image file needed)
    static Drawing.Icon MakeTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var black = new Drawing.SolidBrush(Drawing.Color.FromArgb(17, 17, 17));
            g.FillRectangle(black, 2, 2, 28, 28);
            using var white = new Drawing.Pen(Drawing.Color.White, 4);
            g.DrawEllipse(white, 9, 9, 14, 14);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
