// ----------------------------------------------------------------------------
//  Nombre de la app y carpeta de datos
//
//  La app se llamó "Anotador de capturas" hasta el 2026-10-22. Al arrancar se
//  mueve la carpeta de datos vieja (%LOCALAPPDATA%\AnotadorCapturas) a la
//  nueva (%LOCALAPPDATA%\Popete), y la entrada de "Iniciar con Windows".
// ----------------------------------------------------------------------------
static class AppInfo
{
    public const string Name = "Popete";
    public const string Subtitle = "anotador de capturas";
    public const string FullName = Name + " · " + Subtitle;

    static Stream IconStream() => typeof(AppInfo).Assembly.GetManifestResourceStream("popete.ico");

    /// <summary>Ícono para la bandeja, en el tamaño que pide Windows (elige el mejor del .ico).</summary>
    public static System.Drawing.Icon TrayIcon()
    {
        using var s = IconStream();
        return new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>Ícono para las ventanas WPF.</summary>
    public static readonly ImageSource WindowIcon = BitmapFrame.Create(IconStream(), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string NewRoot = Path.Combine(LocalAppData, "Popete");
    static readonly string LegacyRoot = Path.Combine(LocalAppData, "AnotadorCapturas");

    /// <summary>
    /// Carpeta de datos. Si la carpeta vieja no se pudo mover (por ejemplo, un
    /// archivo en uso), se sigue usando la vieja para no perder el historial.
    /// </summary>
    public static string DataRoot =>
        !Directory.Exists(NewRoot) && Directory.Exists(LegacyRoot) ? LegacyRoot : NewRoot;

    /// <summary>Se llama una vez al arrancar, antes de leer historial o ajustes.</summary>
    public static void MigrateLegacy()
    {
        if (Directory.Exists(LegacyRoot) && !Directory.Exists(NewRoot))
        {
            try { Directory.Move(LegacyRoot, NewRoot); }
            catch { /* se reintenta en el próximo arranque; mientras tanto se usa la vieja */ }
        }
        StartupHelper.MigrateLegacy();
    }
}
