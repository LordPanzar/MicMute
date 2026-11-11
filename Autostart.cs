using Microsoft.Win32;

namespace MicMute;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MicMute";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) != null;
        }
    }

    public static void Set(bool enabled)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
    }
}
