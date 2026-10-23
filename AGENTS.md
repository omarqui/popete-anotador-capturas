# AGENTS.md — Popete (anotador de capturas)

Contexto para continuar este proyecto con un agente de código (Claude Code u otro).

## Qué es

App de bandeja para Windows. Detecta capturas de pantalla mediante el portapapeles, abre un editor WPF para anotarlas y guarda cada captura en un historial que se puede retomar. Es un proyecto WPF de .NET 10 en `src/` (`Popete.csproj`, genera `Popete.exe`). Empezó como file-based app de un solo archivo y se convirtió con `dotnet project convert` el 2026-10-22.

```powershell
dotnet run --project src       # compilar y ejecutar
dotnet build src               # solo compilar (úsalo para verificar cambios)
dotnet publish src             # generar .exe
powershell -File tools\empaquetar-msix.ps1   # paquete MSIX (ver README)
# Store desde CI: .github/workflows/publicar-store.yml (solo manual, workflow_dispatch)
```

Si la app ya está abierta, `dotnet run` sale enseguida por el mutex de instancia única: hay que cerrarla antes desde la bandeja (**Salir**).

Solo compila y corre en **Windows** (`net10.0-windows10.0.19041.0`, WPF y WinForms). En Linux o macOS no se puede verificar el build.

## Estado actual

- Funciona y fue probado en Windows: detección por portapapeles, editor, Ctrl+C con parpadeo.
- Compila sin advertencias (`dotnet build src`).
- **Pendiente de probar a mano:** el historial persistente (guardado automático, "Retomar captura" en la bandeja, ventana de historial), la deduplicación por huella completa y el cierre automático tras Ctrl+C.

## Arquitectura (`src/`)

| Archivo | Tipo | Responsabilidad |
|---|---|---|
| `GlobalUsings.cs` | — | Todos los `using` del proyecto, como `global using`. |
| `AppInfo.cs` | `AppInfo` | Nombre (`Popete`, subtítulo «anotador de capturas») y carpeta de datos. `MigrateLegacy()` mueve `%LOCALAPPDATA%\AnotadorCapturas` a `%LOCALAPPDATA%\Popete` y la entrada de inicio con Windows. `IsPackaged` (con `GetCurrentPackageFullName`): en el MSIX, `DataRoot` es `ApplicationData.Current.LocalFolder` y no se migra nada. |
| `Program.cs` | `Program` | `Main` con `[STAThread]`, mutex de instancia única, `Application` con `ShutdownMode.OnExplicitShutdown`. |
| `CaptureHost.cs` | `CaptureHost` | Ventana *message-only* (`HwndSource`, `HWND_MESSAGE`) con `AddClipboardFormatListener`. Agrupa eventos con un `DispatcherTimer` de 350 ms. Maneja el `NotifyIcon` de WinForms y su menú. `OpenEditor(Session)` es estático y evita abrir dos editores para la misma sesión. |
| `ClipboardHelper.cs` | `ClipboardHelper` | Lee la imagen del portapapeles con reintentos: primero el formato `"PNG"`, si no, el Bitmap vía WinForms reconvertido a PNG. `Signature()` calcula tamaño + SHA-256 de los píxeles en Bgra32. |
| `History.cs` | `History`, `Session`, `SessionMeta`, `TextNote` | Persistencia en `%LOCALAPPDATA%\Popete\Historial\<yyyyMMdd-HHmmss-fff>\`. `Known` es un `HashSet` de huellas (originales y exportadas) que se carga al iniciar. Las escrituras son atómicas (`.tmp` + `File.Move`). |
| `HistoryWindow.cs` | `HistoryWindow` | Tarjetas con miniatura y botones Retomar/Eliminar. Se recarga en `Activated`. |
| `EditorWindow.cs` | `EditorWindow` (`partial`), `Tool` | Editor: campos, constructor, persistencia, deshacer, zoom y teclado. Estructura: `host` (Canvas del tamaño de la captura, con el zoom) → `board` (captura + margen `_pad` alrededor) con `framed`/`_frame` (la captura con esquinas redondeadas, sombra y `_flash`), `_surface` (anotaciones: `InkCanvas` + `Canvas` de textos, toda la tabla) y `_selectionLayer`. Guardado automático 800 ms después de cada cambio. `EditorWindow.Open` es un diccionario estático de editores abiertos por Id de sesión. Colores principal/secundario por familia (lápiz y resaltador); tocar Alt o clic derecho los intercambia. |
| `EditorWindow.Toolbar.cs` | `EditorWindow` | Barra de herramientas (estilo Fluent: grupo de herramientas con "Otras herramientas" — `BuildMoreTools`, un `ContextMenu` con resaltador, línea, rectángulo, texto y texto con fondo; el botón muestra la activa —, par de colores + muestras, grosores 1–3 (2, 4 y 8 px), deshacer/limpiar, recientes, tema, "Copiar y cerrar" (no hay botón de copiar sin cerrar); Guardar como no tiene botón, solo Ctrl+S), barra de estado, `SetTool`, `SetColor`, `SetSize`, `SwapColors`. |
| `EditorWindow.Pointer.cs` | `EditorWindow` | Puntero propio "halo y retícula": el `InkCanvas` usa `Cursors.None` y se dibuja una figura en `_pointerLayer`, encima del `ScrollViewer` y fuera del zoom. Anillo del tamaño real (grosor × zoom) en el color actual con halo blanco/oscuro, 4 marcas y etiqueta de grosor. Se rehace en `UpdatePointer()` al cambiar herramienta, color, grosor o zoom. Seleccionar usa la flecha del sistema. |
| `EditorWindow.Selection.cs` | `EditorWindow` | Selección múltiple de trazos y textos (`_selection` + `_selectedTexts`): lo recién dibujado queda seleccionado; color/grosor se aplican a todo (un solo deshacer con `CompositeChange`), Supr lo borra, Esc la quita. Seleccionar (V): clic con `Strokes.HitTest`, recuadro desde una zona vacía (`Marquee*`, toca = entra, con `FillContainsWithDetail`), Shift suma, Ctrl+A todo. `PickAndDrag` arrastra la selección entera; flechas la corren. |
| `EditorWindow.Numbers.cs` | `EditorWindow` | Numeración automática (las flechas no se numeran: llevan la propiedad ISF `ArrowProperty`): círculo con el número de orden pegado al punto de cada trazo con menor X+Y (el más arriba a la izquierda), corrido medio grosor + 2 px hacia arriba a la izquierda; en textos, su esquina superior izquierda, en `_numbersLayer` (dentro de `_surface`, se exporta). El orden se guarda en el trazo (propiedad ISF `OrderProperty`) y en `TextNote.Orden`. `RefreshNumbers()` corre en cada `MarkChanged()`. Interruptor por captura (`SessionMeta.Numerar`). |
| `EditorWindow.Crop.cs` | `EditorWindow` | Recortar por bordes: con la herramienta Crop (`_cropMode`) se muestra la captura original entera (`ShownRect`/`ShownImage`) con un marco `_pendingCrop` de bordes, esquinas y centro arrastrables. Enter, cambiar de herramienta, copiar, guardar o cerrar → `ApplyCrop` (borra lo que queda totalmente afuera, todo en un `ActionChange`); Esc cancela. `SessionMeta.Crop` sobre `original.png`, que nunca se modifica. |
| `EditorWindow.Drop.cs` | `EditorWindow` | Soltar imágenes: `PreviewDragOver`/`PreviewDrop` en la ventana; `IsEmpty` decide si se reusa la ventana (`ReplaceWith` abre la nueva en la misma posición y cierra esta) o se abre otra. La lectura y apertura está en `CaptureHost.OpenDropped`, que también usa `HistoryWindow`. |
| `EditorWindow.Pan.cs` | `EditorWindow` | Pan con el clic central: `SplitPan` reparte el arrastre entre el desplazamiento del `ScrollViewer` y `_panShift` (traslación del host, sin tocar el layout; máx. 80 % de la vista). Cursor de mano (`Icons.Grab`) dibujado y armado como `.cur` con PNG en `GrabCursor()`, por DPI. |
| `NoteText.cs` | `NoteText` | Texto del editor (hereda `TextBlock`). Modo etiqueta (`Fondo`): `Background` con un rectángulo redondeado del tamaño exacto y la letra en contraste con un `TextEffect`, así `Foreground` sigue siendo el color elegido. `OnRender` y `OnPropertyChanged` de `TextBlock` están sellados. |
| `EditorWindow.AutoClose.cs` | `EditorWindow` | Cierre automático: solo en capturas nuevas del portapapeles (`CaptureHost.OpenEditor(..., autoClose: true)` → `EnableAutoClose`). Si el editor pierde el foco sin cambios, se cierra a los 4 s (volver lo cancela); `MarkTouched` (en `MarkChanged`, `BeginText` y `Save`) lo apaga. No cierra con ventanas propias abiertas. La captura queda en el historial. |
| `NoteStroke.cs` | `NoteStroke` | Trazo con borde blanco fino (`Outline` = 1,5 px por lado): `DrawCore` lo dibuja en blanco y más ancho y encima con su color; el resaltador no lleva borde. `From()` convierte un trazo común (puntos, atributos y propiedades). |
| `ConfirmDialog.cs` | `ConfirmDialog` | Confirmaciones y avisos con el estilo de la app (`Ask`, `Inform`): título, texto y franja con botones en español, tema claro/oscuro. Reemplaza a `MessageBox` en toda la app. |
| `SettingsWindow.cs` | `SettingsWindow` | Ajustes con botones segmentados (respetan el tema); cada cambio se guarda al tocarlo. Se abre desde la barra de estado, Ctrl+, o la bandeja. |
| `EditorWindow.Recents.cs` | `EditorWindow` | Panel de capturas recientes (tira de miniaturas). Siempre arranca oculto; Ctrl+H o el botón lo muestran. Clic abre esa captura con `CaptureHost.OpenEditor`. × por miniatura, "Eliminar todas" (con confirmación) y "Conservar las últimas N" (`AppSettings.MaxCapturas` → `History.Prune`). |
| `EditorWindow.Drawing.cs` | `EditorWindow` | Formas (línea, flecha, rectángulo) y textos. La flecha guarda el recorrido (`_arrowPath`) y al soltar queda recta si `IsNearlyStraight` (desvío ≤ máx(6 px en pantalla, 6 % del largo)) o curva suavizada (`FreeArrow`: media móvil de 5 puntos, punta orientada una cabeza hacia atrás). Shift: recta a 45°. Es un solo trazo: recorrido + punta. La punta es un triángulo relleno (`AddArrowHead`: contorno y un abanico de líneas desde la punta a la base, separadas menos que el grosor), de largo máx(18, grosor × 5). |
| `EditorWindow.Export.cs` | `EditorWindow` | `ExportBounds` (captura + lo que salga al margen) y `Render` (fondo blanco si crece, captura original sin redondear, anotaciones encima); copiar al portapapeles con parpadeo, cierre tras Ctrl+C, guardar como. |
| `Theme.cs` | `Theme` | Tema claro/oscuro: pinceles en `Application.Resources` con claves fijas (`Fondo`, `Superficie`, `Borde`, `Texto`, `Acento`, `Lienzo`...) y estilos de botón en XAML (`XamlReader.Parse`). `Toggle()` alterna claro/oscuro; mientras el ajuste sea `Sistema` (nunca tocado) sigue al tema de Windows (`AppsUseLightTheme` + `SystemEvents.UserPreferenceChanged`). Barra de título oscura con `DwmSetWindowAttribute`. |
| `AppSettings.cs` | `AppSettings`, `ModoTema` | `%LOCALAPPDATA%\Popete\ajustes.json`: tema y `MaxCapturas` (50 por defecto, 0 = sin límite). |
| `Undo.cs` | `IUndo` y sus implementaciones | Pila de deshacer: `StrokeChange`, `AttributeChange`, `ElementAdd`, `ElementRemove`, `TextEdit`, `ActionChange`, `CompositeChange` (varios en un paso), `ClearAllChange`. |
| `StartupHelper.cs` | `StartupHelper` | Inicio con Windows: clave `HKCU\...\Run` como .exe; en el MSIX, la API `StartupTask` (TaskId `PopeteInicio`, declarado en el manifiesto). `Set` devuelve el estado real (el usuario puede bloquearlo en Configuración). |
| `Ui.cs` | `Ui`, `Icons` | Botones con estilo, íconos de trazo (`Path` en grilla 24×24 que toma el color del texto), separadores, grupos y teclas. |

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
10. Las anotaciones cargadas al retomar una sesión **no** entran en la pila de deshacer. "Limpiar" (papelera) pide confirmación y sí se puede deshacer.
11. **Colores de la UI siempre por recurso** (`SetResourceReference` o `{DynamicResource}`), nunca un pincel fijo: es lo que permite cambiar de tema en caliente. Los colores de las anotaciones y del puntero sí son fijos. **Claves:** los colores van sin prefijo (`Icono`, `Acento`...) y los estilos con `Estilo` (`EstiloIcono`, `EstiloPrimario`...). Los colores viven directo en `Application.Resources` y ganan sobre los estilos del diccionario fusionado: un estilo llamado igual que un color hizo fallar el editor con «'#FFD6D6DA' is not a valid value for property 'Style'».
12. **El enum del tema se llama `ModoTema`, no `ThemeMode`:** desde .NET 9, `Window` tiene una propiedad experimental `ThemeMode` (WPF0001) que tapa el nombre dentro de las ventanas.
13. **Los botones de la barra son `Focusable=False`** (en los estilos): un clic en un color o grosor no debe quitarle el foco al `TextBox` en edición, porque eso dispara `CommitText`.
14. **El puntero no va dentro de `_surface` ni del zoom:** así no sale en la imagen exportada y su contorno mide lo mismo a cualquier zoom.
15. **Renombre a Popete (2026-10-22):** `AppInfo.MigrateLegacy()` corre en `Main` **antes** de tocar `History` o `AppSettings` (sus rutas son `static readonly` y se fijan al primer uso). Si la carpeta vieja no se puede mover, `DataRoot` sigue usando la vieja. `Main` sale si encuentra el mutex viejo (`Local\AnotadorCapturas_InstanciaUnica`) para no correr junto a una versión anterior sobre la misma carpeta.
16. **Coordenadas: tabla en pantalla, captura en disco.** Trazos y textos viven en coordenadas de la tabla (corridos `_pad`), pero `trazos.isf` y `textos.json` se guardan relativos a la esquina de la captura (pueden ser negativos si están en el margen). La conversión está solo en `LoadAnnotations`/`SaveSession` (`ToBoard`/`ToCapture`). Así `_pad` puede cambiar sin romper sesiones guardadas.
17. **El margen no entra en el layout:** `_host` es un `Canvas` del tamaño de lo visible y la tabla se cuelga en (-VisibleRect.X, -VisibleRect.Y); Canvas no recorta ni mide hijos, así "Ajustar" encuadra la captura y el margen queda dibujable alrededor.
18. **Exportar no usa el `Image` del editor:** `Render` dibuja `_image` directo (el del editor tiene el recorte redondeado, que es solo visual) y encima un `VisualBrush` de `_surface` con `Viewbox = ExportBounds()`.
19. **Alt solo cuenta como "toque":** se intercambia en el KeyUp si no hubo otra tecla, clic ni cambio de ventana entre medio (Alt+Tab, Alt+F4 y AltGr no lo disparan). Intercambiar colores **no** pinta la selección; elegir un color de la paleta sí.
20. **Ícono:** `assets\popete.svg` → `tools\generar-icono.ps1` → `assets\popete.ico` (PNG por tamaño, 16–256). El `.csproj` lo usa como `ApplicationIcon` y como recurso embebido (`popete.ico`) para la bandeja y las ventanas. Regenerar si cambia el SVG; el script solo entiende rect, circle, ellipse, path, line y polyline.
21. **La tabla está anclada a la captura ORIGINAL** (original + `_pad` alrededor) y no se mueve al recortar: solo cambian `_crop`, `_image` (un `CroppedBitmap`) y `VisibleRect`; `LayoutForImage()` reubica la imagen y el host. Así ninguna coordenada cambia, y los trazos guardados en la pila de deshacer siguen en su lugar. Exportar usa `VisibleRect` como "captura".
22. **Deshacer/rehacer:** todo `IUndo` tiene `Undo()` y `Redo()`; ambos corren con `_suppress = true`. `PushUndo` vacía `_redo`; `Redo()` vuelve a apilar en `_undo` directamente para no vaciarlo.
23. **Selección automática con tiempo:** `SelectStrokes(..., auto: true)` (después de dibujar) arranca un temporizador de `AppSettings.SegundosSeleccion`; la que pone la herramienta Seleccionar no se suelta sola. Cada retoque de color/grosor reinicia la cuenta.
24. **Textos clicables:** `_textLayer` es hit-testable (sin fondo, así los clics en vacío llegan a la tinta). Con Seleccionar, un clic sobre un texto lo elige y arrastrar lo mueve; con otras herramientas no hace nada; doble clic lo edita (`EditText`). Consecuencia: no se puede empezar un trazo justo encima de un texto.
25. **Estilos implícitos** (sin `x:Key`) en `Theme.StylesXaml` para `ToolTip`, `ContextMenu`, `MenuItem`, `ScrollBar` y `ScrollViewer`: toda la app los toma con el tema. El menú de la bandeja (WinForms) usa `CaptureHost.TrayColors` y se rehace en `Theme.Changed`.
26. **Ícono pequeño:** `assets\popete-pequeno.svg` (sin ceja ni sonrisa) se usa para 16–24 px; `generar-icono.ps1` lo toma solo.
27. **Números clicables:** `_numbersLayer` es hit-testable (sin fondo); cada círculo lleva su trazo o texto y `NumberClicked` lo selecciona con cualquier herramienta. Consecuencia: no se puede empezar un trazo justo encima de un número.
28. **Trazos mínimos:** `IsTiny` (menos de 5 px en pantalla) descarta el trazo a mano en `StrokeCollected`; `StrokesChanged` no le registra deshacer. Las formas usan el mismo umbral.
29. **Mientras se recorta, la vista no es la exportación:** `LayoutForImage`, `UpdateFrameClip` y `FitZoom` usan `ShownRect`/`ShownImage` (la captura entera); `Render` y `ExportBounds` siguen con `VisibleRect`/`_image` (el recorte aplicado). Por eso copiar o guardar aplican antes el marco. El marco se dibuja en `_selectionLayer` desde `UpdateSelectionVisual`.
30. **Todos los trazos son `NoteStroke`:** el ISF guarda trazos comunes, así que `LoadAnnotations` los convierte; el `InkCanvas` agrega uno común al terminar un trazo a mano y `StrokeCollected` lo reemplaza con `_suppress` y registra ahí el deshacer (por eso `StrokesChanged` ignora un agregado suelto que no sea `NoteStroke`). Las formas nacen como `NoteStroke`. Mientras se dibuja a mano, el trazo en vivo todavía no tiene borde.
31. **Nada de `MessageBox`:** se ve clásico y con «Yes/No». Usar `ConfirmDialog`.
32. **MSIX (`packaging\`, `tools\empaquetar-msix.ps1`):** dentro del paquete, `HKCU\...\Run` queda virtualizado y no arranca nada al iniciar sesión; por eso `StartupHelper` usa `StartupTask` y el `TargetFramework` lleva la versión de Windows (proyecciones WinRT). Las escrituras en `%LOCALAPPDATA%` también se redirigen, y el Explorador no ve esa redirección: `AppInfo.DataRoot` usa la carpeta real del paquete. `AppxManifest.xml` es una plantilla (`$IDENTITY_NAME$`, `$PUBLISHER$`...). Los logos de `packaging\Assets` los genera `generar-icono.ps1` con calificadores `scale-*`/`targetsize-*`, que resuelve `resources.pri`; el script le quita `<packaging>` al priconfig para que no parta el `.pri` por escala (eso solo sirve en un bundle). La versión es `N.N.N.0`. `-Store` deja el paquete sin firmar (lo firma la Store); sin `-Store`, firma con un certificado propio `CN=Popete Prueba` en `CurrentUser\My`. Los `.ps1` van sin tildes: PowerShell 5.1 lee mal UTF-8 sin BOM.
33. **El portapapeles es de otros programas: nunca confiar en él.** La certificación de la Store tumbó la 1.0.0.0: había datos en formato `"PNG"` que no eran un PNG y `Decode` lanzó `NotSupportedException` sin atrapar. Lo que venga del portapapeles o de un arrastre se lee con `ClipboardHelper.TryDecode` (null si no se puede), con el bitmap como alternativa. Además, `Program` atrapa `DispatcherUnhandledException`, lo anota con `AppInfo.LogError` en `errores.log` y la app sigue en la bandeja.
34. **Ratón en el lienzo:** clic central arrastra la vista (`EditorWindow.Pan.cs`: captura el mouse en `_scroll` y esconde `_pointerLayer` mientras dura; funciona aunque la captura entre entera, con `_panShift` como `RenderTransform` de `_host`; "Ajustar" lo vuelve a cero). Doble clic izquierdo copia y cierra, salvo sobre un texto (lo edita) o con un cuadro de texto abierto. El primer clic del doble clic deja un trazo mínimo que `IsTiny` descarta.

## Convenciones

- Textos de UI y comentarios en **español**.
- Un archivo por tipo. `EditorWindow` es `partial` y se reparte por área (barra, puntero, recientes, dibujo, exportar). Un área nueva va en su propio `EditorWindow.<Área>.cs`.
- Diseño: estilo Windows 11 (propuesta A del canvas https://claude.ai/artifact/HmK39unoWaSLtBNUotKNYs). Íconos de trazo, sin emoji.
- Hilo único: todo corre en el dispatcher de WPF, incluidos los eventos del `NotifyIcon`. No hay concurrencia.

## Prueba manual (no hay tests automatizados)

1. `dotnet run --project src` → aparece el ícono y el aviso en la bandeja.
2. Win+Shift+S → se abre el editor. Impr Pant → se abre el editor.
3. Dibujar con cada herramienta, usar Shift en línea, flecha y rectángulo, escribir texto, Ctrl+Z.
4. Ctrl+C → parpadeo, el editor se cierra solo (~450 ms) y **no** se abre un editor nuevo. Pegar en Paint u otro programa y verificar las anotaciones. Un clic en el ícono de la bandeja abre la última captura (o el historial si no hay ninguna).
5. Esperar unos minutos, o desconectar y reconectar RDP si aplica → la última captura **no** se reabre.
6. Cerrar el editor → bandeja → Retomar captura → las anotaciones siguen ahí y se pueden editar.
7. Ver historial → eliminar una captura (con su editor cerrado).
8. Reiniciar la app → la imagen que siga en el portapapeles no abre editor; el historial persiste.
9. Puntero: con cada herramienta, sobre zonas claras y oscuras de la captura, se ve; con lápiz y resaltador mide lo mismo que el trazo (probar grosores 1–3 y zoom). Fuera de la imagen vuelve la flecha normal.
10. Tema: botón de luna → Oscuro / Claro / Usar el tema de Windows. Cambia en caliente (editor e historial, barra de título incluida). Con "tema de Windows", cambiarlo en Configuración › Personalización › Colores se refleja solo. Persiste al reiniciar.
11. Recientes: el editor abre con el panel oculto; Ctrl+H lo muestra, clic en una miniatura abre esa captura, la actual sale marcada.

## Ideas pendientes (backlog)

- Desenfocar o pixelar zonas (datos sensibles).
- Recortar la imagen.
- Mover y editar textos y formas existentes (hoy solo se borran trazos completos y se deshace).
- Rehacer (Ctrl+Y).
- Atajo global propio para capturar región (`Graphics.CopyFromScreen`) sin depender de Recortes.
- Limpieza automática del historial configurable (por ejemplo, más de 30 días).
- Ícono propio en lugar de `SystemIcons.Application`.
- Más ajustes en `ajustes.json`: colores, grosor y carpeta de guardado (el tema ya está).
- Menús contextuales, tooltips y barras de desplazamiento siguen con el estilo clásico de WPF (claros también en tema oscuro).
