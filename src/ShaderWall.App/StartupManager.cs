using Microsoft.Win32;

namespace ShaderWallApp;

// Adds or removes ShaderWall from HKEY_CURRENT_USER\...\Run: the programs Windows starts
// when *you* log in. HKEY_CURRENT_USER means it only affects your account and needs no admin rights.
static class StartupManager
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "ShaderWall";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Enable()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);   // opens it, or creates it if missing
        // Quotes around the path in case it contains spaces; --tray = start hidden
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
