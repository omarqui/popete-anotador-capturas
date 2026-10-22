// ----------------------------------------------------------------------------
//  Inicio con Windows (HKCU\...\Run)
// ----------------------------------------------------------------------------
static class StartupHelper
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AnotadorCapturas";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) != null;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(ValueName, false);
    }
}
