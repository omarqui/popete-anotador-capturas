# AGENTS.md — Anotador de capturas

Contexto para continuar este proyecto con un agente de código (Claude Code u otro).

## Qué es

App de bandeja para Windows. Detecta capturas de pantalla mediante el portapapeles, abre un editor WPF para anotarlas y guarda cada captura en un historial que se puede retomar. Es un proyecto WPF de .NET 10 en `src/` (`AnotadorCapturas.csproj`). Empezó como file-based app de un solo archivo y se convirtió con `dotnet project convert` el 2026-10-22.

```powershell
dotnet run --project src       # compilar y ejecutar
dotnet build src               # solo compilar (úsalo para verificar cambios)
dotnet publish src             # generar .exe
```

Si la app ya está abierta, `dotnet run` sale enseguida por el mutex de instancia única: hay que cerrarla antes desde la bandeja (**Salir**).

Solo compila y corre en **Windows** (`net10.0-windows`, WPF y WinForms). En Linux o macOS no se puede verificar el build.

## Estado actual

- Funciona y fue probado en Windows: detección por portapapeles, editor, Ctrl+C con parpadeo.
- Compila sin advertencias (`dotnet build src`).
- **Pendiente de probar a mano:** el historial persistente (guardado automático, "Retomar captura" en la bandeja, ventana de historial), la deduplicación por huella completa y el cierre automático tras Ctrl+C.

## Arquitectura (`src/`)

| Archivo | Tipo | Responsabilidad |
|---|---|---|
| `GlobalUsings.cs` | — | Todos los `using` del proyecto, como `global using`. |
| `Program.cs` | `Program` | `Main` con `[STAThread]`, mutex de instancia única, `Application` con `ShutdownMode.OnExplicitShutdown`. |
| `CaptureHost.cs` | `CaptureHost` | Ventana *message-only* (`HwndSource`, `HWND_MESSAGE`) con `AddClipboardFormatListener`. Agrupa eventos con un `DispatcherTimer` de 350 ms. Maneja el `NotifyIcon` de WinForms y su menú. `OpenEditor(Session)` es estático y evita abrir dos editores para la misma sesión. |
| `ClipboardHelper.cs` | `ClipboardHelper` | Lee la imagen del portapapeles con reintentos: primero el formato `"PNG"`, si no, el Bitmap vía WinForms reconvertido a PNG. `Signature()` calcula tamaño + SHA-256 de los píxeles en Bgra32. |
| `History.cs` | `History`, `Session`, `SessionMeta`, `TextNote` | Persistencia en `%LOCALAPPDATA%\AnotadorCapturas\Historial\<yyyyMMdd-HHmmss-fff>\`. `Known` es un `HashSet` de huellas (originales y exportadas) que se carga al iniciar. Las escrituras son atómicas (`.tmp` + `File.Move`). |
| `HistoryWindow.cs` | `HistoryWindow` | Tarjetas con miniatura y botones Retomar/Eliminar. Se recarga en `Activated`. |
| `EditorWindow.cs` | `EditorWindow` (`partial`), `Tool` | Editor: campos, constructor, persistencia, deshacer, zoom y teclado. `_surface` (Grid con `Image` + `InkCanvas` + `Canvas` de textos) es lo que se exporta. `_flash` es una capa encima, fuera de `_surface`, para el parpadeo. Guardado automático 800 ms después de cada cambio. `EditorWindow.Open` es un diccionario estático de editores abiertos por Id de sesión. |
| `EditorWindow.Toolbar.cs` | `EditorWindow` | Barra de herramientas, colores, grosor y `SetTool`. |
| `EditorWindow.Drawing.cs` | `EditorWindow` | Formas (línea, flecha, rectángulo) y textos. |
| `EditorWindow.Export.cs` | `EditorWindow` | `Render`, copiar al portapapeles con parpadeo, cierre tras Ctrl+C, guardar como. |
| `Undo.cs` | `IUndo` y sus implementaciones | Pila de deshacer: `StrokeChange`, `ElementAdd`, `ClearAllChange`. |
| `StartupHelper.cs` | `StartupHelper` | Clave `HKCU\...\Run`. |
| `Ui.cs` | `Ui.Btn` | Helper para crear botones. |

## Decisiones y trampas conocidas (no las deshagas)

1. **Propiedades del `.csproj`:**
   - `PublishAot=false` es obligatorio: WPF no soporta AOT (los file-based apps lo activaban por defecto).
   - `EnableDefaultPageItems`, `EnableDefaultApplicationDefinition`, `EnableDefaultEmbeddedResourceItems` y `EnableDefaultNoneItems` en `false`: la UI se construye en código y no debe recogerse ningún `.xaml` ni `.resx` suelto. Cuando era un file-based app, el build ya falló así con un proyecto Xamarin en `Downloads`.
   - `ImplicitUsings=disable`: WPF y WinForms juntos generan ambigüedades (`Application`, `Clipboard`, `MessageBox`, `Image`, `Button`...). Los `using` viven en `GlobalUsings.cs`. **WinForms y System.Drawing siempre se escriben con el nombre completo** (`System.Windows.Forms.NotifyIcon`, `System.Drawing.Bitmap`) y nunca se importan con `using`.
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
- Un archivo por tipo. `EditorWindow` es `partial` y se reparte por área (barra, dibujo, exportar). Si un área nueva crece (por ejemplo, el puntero personalizado), va en su propio `EditorWindow.<Área>.cs`.
- Hilo único: todo corre en el dispatcher de WPF, incluidos los eventos del `NotifyIcon`. No hay concurrencia.

## Prueba manual (no hay tests automatizados)

1. `dotnet run --project src` → aparece el ícono y el aviso en la bandeja.
2. Win+Shift+S → se abre el editor. Impr Pant → se abre el editor.
3. Dibujar con cada herramienta, usar Shift en línea, flecha y rectángulo, escribir texto, Ctrl+Z.
4. Ctrl+C → parpadeo, el editor se cierra solo (~450 ms) y **no** se abre un editor nuevo. Pegar en Paint u otro programa y verificar las anotaciones. El botón "Copiar" copia sin cerrar.
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
