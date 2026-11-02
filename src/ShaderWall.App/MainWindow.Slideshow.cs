using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ShaderWallApp;

// Slideshow: switch to the next library shader as the PC goes to sleep or is locked
// (so the new one is already there when you come back), or every N minutes.
// This is EVENT-DRIVEN: we do nothing until Windows tells us something happened
// (unlike the engine's pause check, which POLLS: it asks Windows every frame).
public partial class MainWindow
{
    readonly DispatcherTimer slideshowTimer = new();   // runs its Tick on the UI thread
    readonly Random random = new();

    void SetupSlideshow()
    {
        // Windows sends WM_POWERBROADCAST / WM_WTSSESSION_CHANGE; .NET turns them into these events
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        slideshowTimer.Tick += (s, e) =>
        {
            if (!wallpaperPaused) NextShader(force: false);
        };
        ApplySlideshowTimer();
    }

    // SystemEvents are static: if we don't unsubscribe, they keep this window alive (a memory leak)
    void TeardownSlideshow()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        slideshowTimer.Stop();
    }

    // Switch on the way OUT, not on the way back: the engine keeps running behind the
    // lock screen and swaps the shader there, so you never see it change.
    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend || !settings.SlideshowOnSleep) return;

        // These events arrive on a background thread. Invoke (not InvokeAsync) waits until
        // the switch has been sent to the engine, before Windows finishes going to sleep.
        Dispatcher.Invoke(() => NextShader(force: false));
    }

    void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionLock || !settings.SlideshowOnLock) return;
        Dispatcher.Invoke(() => NextShader(force: false));
    }

    void ApplySlideshowTimer()
    {
        slideshowTimer.Stop();
        if (settings.SlideshowEveryMinutes > 0)
        {
            slideshowTimer.Interval = TimeSpan.FromMinutes(settings.SlideshowEveryMinutes);
            slideshowTimer.Start();
        }
    }

    // force = the "Skip" button: switch even if no wallpaper is on right now
    void NextShader(bool force)
    {
        if (!force && !settings.WallpaperOn) return;   // you stopped the wallpaper: leave it off

        // Only shaders that pass the quick check (e.g. have a mainImage)
        var candidates = library
            .Where(s => CompatibilityChecker.CheckSource(s.Image, s.Common, null).CanRun)
            .ToList();
        if (candidates.Count == 0) return;
        if (candidates.Count == 1 && candidates[0].Name == runningName) return;   // nothing else to switch to

        SavedShader next;
        if (settings.SlideshowRandom)
        {
            // Random, but never the same one twice in a row
            var others = candidates.Where(s => s.Name != runningName).ToList();
            next = others[random.Next(others.Count)];
        }
        else
        {
            // In order: the one after the current shader (or the first, if none is running)
            int current = candidates.FindIndex(s => s.Name == runningName);
            next = candidates[(current + 1) % candidates.Count];
        }

        SetAsWallpaper(next);
    }

    // ---------- Settings controls ----------

    void SlideshowSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (applyingSettings) return;
        settings.SlideshowOnSleep = SleepSwitch.IsChecked == true;
        settings.SlideshowOnLock = LockSwitch.IsChecked == true;
        settings.Save();
    }

    void IntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingSettings || IntervalBox == null) return;
        settings.SlideshowEveryMinutes = int.Parse((string)((ComboBoxItem)IntervalBox.SelectedItem).Tag);
        settings.Save();
        ApplySlideshowTimer();
    }

    void Order_Checked(object sender, RoutedEventArgs e)
    {
        if (applyingSettings || OrderRandom == null) return;
        settings.SlideshowRandom = OrderRandom.IsChecked == true;
        settings.Save();
    }

    void SkipShader_Click(object sender, RoutedEventArgs e) => NextShader(force: true);

    // Put the saved slideshow settings into the controls (called once at start-up)
    void ApplySlideshowSettingsToControls()
    {
        SleepSwitch.IsChecked = settings.SlideshowOnSleep;
        LockSwitch.IsChecked = settings.SlideshowOnLock;
        OrderRandom.IsChecked = settings.SlideshowRandom;
        OrderInOrder.IsChecked = !settings.SlideshowRandom;
        foreach (ComboBoxItem item in IntervalBox.Items)
            if ((string)item.Tag == settings.SlideshowEveryMinutes.ToString())
                IntervalBox.SelectedItem = item;
        if (IntervalBox.SelectedItem == null) IntervalBox.SelectedIndex = 0;
    }
}
