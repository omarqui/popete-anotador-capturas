// ----------------------------------------------------------------------------
//  Inicio con Windows (HKCU\...\Run)
// ----------------------------------------------------------------------------
static class StartupHelper
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Popete";
    const string LegacyValueName = "AnotadorCapturas";

    /// <summary>Si estaba activado con el nombre viejo, lo pasa al nuevo apuntando a este .exe.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue(LegacyValueName) == null) return;
            k.DeleteValue(LegacyValueName, false);
            Set(true);
        }
        catch { /* sin permisos sobre la clave: se deja como está */ }
    }

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
