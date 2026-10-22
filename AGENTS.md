# AGENTS.md — Anotador de capturas

Contexto para continuar este proyecto con un agente de código (Claude Code u otro).

## Qué es

App de bandeja para Windows. Detecta capturas de pantalla mediante el portapapeles, abre un editor WPF para anotarlas y guarda cada captura en un historial que se puede retomar. Toda la app es **un solo archivo**, `AnotadorCapturas.cs`, ejecutado como file-based app de .NET 10. No hay `.csproj`.

```powershell
dotnet run AnotadorCapturas.cs       # compilar y ejecutar
dotnet build AnotadorCapturas.cs     # solo compilar (úsalo para verificar cambios)
dotnet publish AnotadorCapturas.cs   # generar .exe
```

Solo compila y corre en **Windows** (`net10.0-windows`, WPF y WinForms). En Linux o macOS no se puede verificar el build.

## Estado actual

- Funciona y fue probado en Windows: detección por portapapeles, editor, Ctrl+C con parpadeo.
- **Pendiente de verificar en Windows:** el historial persistente (guardado automático, "Retomar captura" en la bandeja, ventana de historial) y la nueva deduplicación por huella completa. Se escribieron sin poder compilar. El primer paso al retomar es `dotnet build AnotadorCapturas.cs` y corregir lo que salga.

## Arquitectura (todo dentro de `AnotadorCapturas.cs`)

| Tipo | Responsabilidad |
|---|---|
| `Program` | `Main` con `[STAThread]`, mutex de instancia única, `Application` con `ShutdownMode.OnExplicitShutdown`. |
| `CaptureHost` | Ventana *message-only* (`HwndSource`, `HWND_MESSAGE`) con `AddClipboardFormatListener`. Agrupa eventos con un `DispatcherTimer` de 350 ms. Maneja el `NotifyIcon` de WinForms y su menú. `OpenEditor(Session)` es estático y evita abrir dos editores para la misma sesión. |
| `ClipboardHelper` | Lee la imagen del portapapeles con reintentos: primero el formato `"PNG"`, si no, el Bitmap vía WinForms reconvertido a PNG. `Signature()` calcula tamaño + SHA-256 de los píxeles en Bgra32. |
| `History`, `Session`, `SessionMeta`, `TextNote` | Persistencia en `%LOCALAPPDATA%\AnotadorCapturas\Historial\<yyyyMMdd-HHmmss-fff>\`. `Known` es un `HashSet` de huellas (originales y exportadas) que se carga al iniciar. Las escrituras son atómicas (`.tmp` + `File.Move`). |
| `HistoryWindow` | Tarjetas con miniatura y botones Retomar/Eliminar. Se recarga en `Activated`. |
| `EditorWindow` | Editor. `_surface` (Grid con `Image` + `InkCanvas` + `Canvas` de textos) es lo que se exporta. `_flash` es una capa encima, fuera de `_surface`, para el parpadeo. Guardado automático 800 ms después de cada cambio. `EditorWindow.Open` es un diccionario estático de editores abiertos por Id de sesión. |
| `IUndo` y sus implementaciones | Pila de deshacer: `StrokeChange`, `ElementAdd`, `ClearAllChange`. |
| `StartupHelper` | Clave `HKCU\...\Run`. |
| `Ui.Btn` | Helper para crear botones. |

## Decisiones y trampas conocidas (no las deshagas)

1. **Directivas al inicio del archivo:**
   - `PublishAot=false` es obligatorio: los file-based apps activan AOT por defecto y WPF no lo soporta.
   - `EnableDefaultPageItems`, `EnableDefaultApplicationDefinition`, `EnableDefaultEmbeddedResourceItems` y `EnableDefaultNoneItems` en `false`: sin ellas, el build recoge `.xaml` y `.resx` de la carpeta y subcarpetas. Ya falló así con un proyecto Xamarin en `Downloads`.
   - `ImplicitUsings=disable`: WPF y WinForms juntos generan ambigüedades (`Application`, `Clipboard`, `MessageBox`, `Image`, `Button`...). Por eso **WinForms y System.Drawing siempre se escriben con el nombre completo** (`System.Windows.Forms.NotifyIcon`, `System.Drawing.Bitmap`) y nunca se importan con `using`.
   - No importes `System.Windows.Shapes`: su `Path` choca con `System.IO.Path`.
2. **Sin top-level statements:** se necesita `[STAThread]` en `Main`, y los top-level statements no permiten atributos.
3. **`PreviewKeyDown`, no `KeyDown`, en el editor:** `InkCanvas` tiene command bindings propios para Copy/Cut/Paste y se queda con Ctrl+C antes de que llegue a la ventana.
4. **Deduplicación:** solo se abre una imagen si su huella no está en `History.Known`. Programas como RDP, el historial del portapapeles de Windows o gestores de portapapeles vuelven a publicar la misma imagen; antes había una ventana de 5 s y la última captura se reabría una y otra vez. Al copiar desde el editor, la huella de la imagen exportada se registra **antes** de `Clipboard.SetDataObject`. La huella se calcula decodificando el mismo PNG que va al portapapeles, para que coincida con lo que se leerá después.
5. **Lectura del portapapeles con WinForms:** `System.Windows.Clipboard.GetImage()` de WPF falla con el alfa de CF_DIB en capturas. Por eso se pasa por PNG.
6. **`Ui.Btn` recibe un `Action`:** pasar un *method group* que devuelve algo (por ejemplo, `CopyToClipboard`, que devuelve `bool`) da `CS0407`. Hay que envolverlo en una lambda.
7. **Exportar con `VisualBrush` + `RenderTargetBitmap`** de `_surface` y `Viewbox` absoluto. Así se exporta a resolución nativa sin el zoom (`LayoutTransform`), que está en un contenedor padre.
8. **1 DIP = 1 píxel** dentro de `_surface`. El zoom inicial es `min(1/DpiScale, ajuste a la ventana)`.
9. **El guardado automático se pospone mientras hay un `TextBox` en edición** (`_editing != null`), para que el cuadro de texto no salga en `anotada.png`.
10. Las anotaciones cargadas al retomar una sesión **no** entran en la pila de deshacer. "Limpiar" sí se puede deshacer.

## Convenciones

- Textos de UI y comentarios en **español**.
- Todo vive en un solo archivo mientras sea manejable. Si crece demasiado, proponer `dotnet project convert` antes de partirlo a mano.
- Hilo único: todo corre en el dispatcher de WPF, incluidos los eventos del `NotifyIcon`. No hay concurrencia.

## Prueba manual (no hay tests automatizados)

1. `dotnet run AnotadorCapturas.cs` → aparece el ícono y el aviso en la bandeja.
2. Win+Shift+S → se abre el editor. Impr Pant → se abre el editor.
3. Dibujar con cada herramienta, usar Shift en línea, flecha y rectángulo, escribir texto, Ctrl+Z.
4. Ctrl+C → parpadeo y **no** se abre un editor nuevo. Pegar en Paint u otro programa y verificar las anotaciones.
5. Esperar unos minutos, o desconectar y reconectar RDP si aplica → la última captura **no** se reabre.
6. Cerrar el editor → bandeja → Retomar captura → las anotaciones siguen ahí y se pueden editar.
7. Ver historial → eliminar una captura (con su editor cerrado).
8. Reiniciar la app → la imagen que siga en el portapapeles no abre editor; el historial persiste.

## Ideas pendientes (backlog)

- Desenfocar o pixelar zonas (datos sensibles).
- Recortar la imagen.
- Mover y editar textos y formas existentes (hoy solo se borran trazos completos y se deshace).
- Rehacer (Ctrl+Y).
- Atajo global propio para capturar región (`Graphics.CopyFromScreen`) sin depender de Recortes.
- Limpieza automática del historial configurable (por ejemplo, más de 30 días).
- Ícono propio en lugar de `SystemIcons.Application`.
- Ajustes persistentes: colores, grosor y carpeta de guardado.
