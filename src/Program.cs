// ============================================================================
//  Popete · anotador de capturas (.NET 10, WPF + WinForms)
//
//  Ejecutar:   dotnet run --project src
//  Publicar:   dotnet publish src   (genera un .exe)
//
//  Queda en la bandeja del sistema. Cada imagen NUEVA que llega al
//  portapapeles (Impr Pant, Win+Shift+S, Recortes...) abre un editor.
//  Cada captura se guarda en un historial con sus anotaciones y puede
//  retomarse desde el ícono de la bandeja.
//
//  Historial: %LOCALAPPDATA%\Popete\Historial\<fecha-hora>\
//     original.png   captura sin editar
//     trazos.isf     trazos (lápiz, resaltador, formas)
//     textos.json    textos
//     anotada.png    imagen con las anotaciones
//     miniatura.png  vista previa pequeña
//     meta.json      fechas, tamaño y huellas para detectar duplicados
// ============================================================================

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Popete_InstanciaUnica", out bool esPrimera);
        if (!esPrimera) return; // ya hay una instancia corriendo
        // Tampoco junto a una versión anterior con el nombre viejo: usaría la misma carpeta de datos
        if (Mutex.TryOpenExisting(@"Local\AnotadorCapturas_InstanciaUnica", out var vieja)) { vieja.Dispose(); return; }

        AppInfo.MigrateLegacy();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Red de seguridad: un error suelto (por ejemplo, algo raro en el portapapeles) se anota
        // en errores.log y la app sigue en la bandeja en vez de cerrarse.
        app.DispatcherUnhandledException += (s, e) =>
        {
            AppInfo.LogError(e.Exception);
            e.Handled = true;
        };
        Theme.Init();
        using var host = new CaptureHost(app);
        host.Start();
        app.Run();
    }
}
