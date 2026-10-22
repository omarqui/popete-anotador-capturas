// ============================================================================
//  Anotador de capturas (.NET 10, WPF + WinForms)
//
//  Ejecutar:   dotnet run --project src
//  Publicar:   dotnet publish src   (genera un .exe)
//
//  Queda en la bandeja del sistema. Cada imagen NUEVA que llega al
//  portapapeles (Impr Pant, Win+Shift+S, Recortes...) abre un editor.
//  Cada captura se guarda en un historial con sus anotaciones y puede
//  retomarse desde el ícono de la bandeja.
//
//  Historial: %LOCALAPPDATA%\AnotadorCapturas\Historial\<fecha-hora>\
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
        using var mutex = new Mutex(true, @"Local\AnotadorCapturas_InstanciaUnica", out bool esPrimera);
        if (!esPrimera) return; // ya hay una instancia corriendo

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new CaptureHost(app);
        host.Start();
        app.Run();
    }
}
