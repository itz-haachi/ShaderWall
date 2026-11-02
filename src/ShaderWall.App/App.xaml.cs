using System.Linq;
using System.Windows;

namespace ShaderWallApp;

public partial class App : Application
{
    // Instead of always opening MainWindow (StartupUri), choose:
    //   normal start        -> show the window
    //   started by Windows  -> "--tray": stay hidden, bring back the last wallpaper
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;

        if (e.Args.Contains("--tray"))
            window.StartInTray();
        else
            window.Show();
    }
}
