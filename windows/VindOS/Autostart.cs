using Microsoft.Win32;

namespace VindOS;

static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool Enabled
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue("vindOS") as string == Command; }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue("vindOS", Command); else k.DeleteValue("vindOS", false);
            Log.Write($"autostart {(value ? "enabled" : "disabled")}");
        }
    }
}
