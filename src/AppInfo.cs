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

    /// <summary>Si corre instalada como paquete MSIX (Microsoft Store o .msix de prueba).</summary>
    public static readonly bool IsPackaged = DetectPackage();

    const int ErrorInsufficientBuffer = 122; // con paquete; sin paquete devuelve APPMODEL_ERROR_NO_PACKAGE

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(ref int length, IntPtr name);

    static bool DetectPackage()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == ErrorInsufficientBuffer;
    }

    /// <summary>
    /// Carpeta de datos. Si la carpeta vieja no se pudo mover (por ejemplo, un
    /// archivo en uso), se sigue usando la vieja para no perder el historial.
    /// En el MSIX es la carpeta propia del paquete: Windows redirige igual las
    /// escrituras en %LOCALAPPDATA%, pero el Explorador ("Abrir carpeta del
    /// historial") no vería esa redirección.
    /// </summary>
    public static string DataRoot =>
        IsPackaged ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
        : !Directory.Exists(NewRoot) && Directory.Exists(LegacyRoot) ? LegacyRoot : NewRoot;

    /// <summary>
    /// Anota un error inesperado en errores.log (carpeta de datos). Solo datos técnicos
    /// (tipo, mensaje y pila); si pasa de 512 KB se empieza de nuevo.
    /// </summary>
    public static void LogError(Exception e)
    {
        try
        {
            var path = Path.Combine(DataRoot, "errores.log");
            Directory.CreateDirectory(DataRoot);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* sin disco o sin permisos: no hay dónde anotarlo */ }
    }

    /// <summary>Se llama una vez al arrancar, antes de leer historial o ajustes.</summary>
    public static void MigrateLegacy()
    {
        if (IsPackaged) return; // el paquete arranca con su propia carpeta y su propio inicio con Windows
        if (Directory.Exists(LegacyRoot) && !Directory.Exists(NewRoot))
        {
            try { Directory.Move(LegacyRoot, NewRoot); }
            catch { /* se reintenta en el próximo arranque; mientras tanto se usa la vieja */ }
        }
        StartupHelper.MigrateLegacy();
    }
}
