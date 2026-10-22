// ----------------------------------------------------------------------------
//  Inicio con Windows
//
//  Como .exe suelto: clave HKCU\...\Run. Instalada como MSIX, esa clave queda
//  virtualizada dentro del paquete y Windows no la lee al iniciar sesión: se usa
//  la tarea de inicio declarada en el manifiesto (packaging\AppxManifest.xml,
//  TaskId "PopeteInicio") con la API StartupTask. El usuario puede apagarla
//  desde Configuración › Aplicaciones › Inicio, y entonces la app ya no puede
//  volver a encenderla (estado DisabledByUser): Set devuelve el estado real.
// ----------------------------------------------------------------------------
static class StartupHelper
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Popete";
    const string LegacyValueName = "AnotadorCapturas";
    const string TaskId = "PopeteInicio";

    /// <summary>Aviso para cuando Windows no deja encender el inicio (lo apagó el usuario en Configuración).</summary>
    public const string BlockedMessage =
        "Windows tiene a Popete desactivado al iniciar sesión. Actívalo en Configuración › Aplicaciones › Inicio.";

    /// <summary>Si estaba activado con el nombre viejo, lo pasa al nuevo apuntando a este .exe.</summary>
    public static void MigrateLegacy()
    {
        if (AppInfo.IsPackaged) return;
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
        if (AppInfo.IsPackaged)
        {
            try { return IsOn(Task().State); }
            catch { return false; }
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) != null;
    }

    /// <summary>Enciende o apaga el inicio con Windows. Devuelve cómo quedó de verdad.</summary>
    public static bool Set(bool on)
    {
        if (AppInfo.IsPackaged)
        {
            try
            {
                var task = Task();
                if (!on) { task.Disable(); return false; }
                return IsOn(task.RequestEnableAsync().AsTask().GetAwaiter().GetResult());
            }
            catch { return IsEnabled(); }
        }
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(ValueName, false);
        return on;
    }

    static Windows.ApplicationModel.StartupTask Task() =>
        Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();

    static bool IsOn(Windows.ApplicationModel.StartupTaskState state) =>
        state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
}
