# Captura en el lugar — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Con el modo de trabajo «En el lugar», Win+Shift+S abre la selección propia de Popete y la captura se anota exactamente donde estaba en la pantalla.

**Architecture:** Un hook `WH_KEYBOARD_LL` en un hilo propio (`KeyboardHook`) decide con lógica pura (`SnipKeys`) y avisa al dispatcher. `ScreenSnip` congela los monitores y deja elegir un rectángulo; al soltar, `InPlaceWindow` (transparente, del monitor entero) aloja el mismo `EditorView` de siempre en un modo de diseño «en el lugar». `EditorView` deja de depender de `EditorShell` a través de `IEditorHost`, así el mismo editor puede mudarse a una pestaña sin perder el deshacer. La app pasa a PerMonitorV2.

**Tech Stack:** .NET 10, WPF + WinForms, P/Invoke user32/shcore, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-28-captura-en-el-lugar-design.md`

## Global Constraints

- Las reglas de AGENTS.md: WinForms y System.Drawing con nombre completo, sin `System.Windows.Shapes` importado, sin `MessageBox`, colores por recurso (`SetResourceReference`), textos de UI y comentarios en español, un archivo por tipo, `EditorView` partido por área.
- Verificar cada tarea con `dotnet build src` (sin advertencias) y `dotnet test tests/Popete.Tests`.
- Modo de fábrica: pestañas. Con ese modo no se instala el hook.
- Franja: 48 DIP (dentro del rango 40–60 del diseño). Selección mínima: 5 px por lado. Tecla de relleno: VK 0xE8.

## Review Focus

- Popete ocupado (guardando, exportando) mientras se tipea en otro programa: el teclado no se demora (el hook nunca toca el hilo de WPF).
- Win+Shift+S repetido rápido, o con una selección ya abierta: no abre dos selecciones ni deja ventanas colgadas (`SnipKeys`: repetición de S tragada sin volver a disparar; `ScreenSnip.Active`).
- Captura pegada al borde del monitor o del tamaño del monitor: la franja se corta y la barra va adentro (`InPlaceLayoutTests`).
- Diálogo «Guardar como» o confirmación abiertos desde el editor en el lugar: no cuentan como clic afuera (`ComponentDispatcher.IsThreadModal`, `OwnedWindows`).
- Win y Shift sueltos en otro orden, o Ctrl+Win+Shift+S: no se dispara ni queda una tecla trabada (`SnipKeysTests`).

---

### Task 1: PerMonitorV2 y monitores

**Files:**
- Create: `src/app.manifest`, `src/Monitors.cs`
- Modify: `src/Popete.csproj`, `src/QuickHistoryWindow.cs:134-147`, `src/EditorView.cs` (zoom al cambiar de DPI)

**Interfaces:**
- Produces: `static class Monitors` con `record MonitorInfo(Int32Rect Bounds, Int32Rect Work, double Scale)` (píxeles físicos; `Scale` = DPI/96), `List<MonitorInfo> All()`, `MonitorInfo At(int x, int y)`, `Int32Rect VirtualScreen()`, `void PlaceInPixels(Window w, Int32Rect px)` (fija la ventana en píxeles físicos con `SetWindowPos` al crearse el HWND, antes de mostrarse).

- [ ] **Step 1:** `app.manifest` con `<dpiAwareness>PerMonitorV2</dpiAwareness>` y `<dpiAware>true/pm</dpiAware>`; `<ApplicationManifest>app.manifest</ApplicationManifest>` en el csproj.
- [ ] **Step 2:** `Monitors.cs` (`EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, `SetWindowPos`).
- [ ] **Step 3:** `QuickHistoryWindow.PlaceOnMouseMonitor` usa `Monitors.At(cursor)` y `PlaceInPixels`; `_previewDecodeWidth` con la escala de ese monitor. Se quita `GetDpiForSystem`.
- [ ] **Step 4:** `EditorView.OnDpiChanged`: rehace etiqueta de zoom, modo de escalado, puntero y tarjeta (`SetZoom(_zoom.ScaleX)`).
- [ ] **Step 5:** `dotnet build src` sin advertencias; arrancar y abrir Alt+` y el editor (100 %): igual que antes.
- [ ] **Step 6:** Commit «PerMonitorV2: Popete nítido en cada monitor».

### Task 2: Lógica de las teclas (`SnipKeys`)

**Files:**
- Create: `src/SnipKeys.cs`, `tests/Popete.Tests/SnipKeysTests.cs`

**Interfaces:**
- Produces: `enum SnipKeyAction { Pass, Swallow, Trigger, Cancel }`; `sealed class SnipKeys { bool Selecting { get; set; } SnipKeyAction OnKey(int vk, bool down, bool injected); }`. Lleva el estado de Win (0x5B/0x5C), Shift (0x10/0xA0/0xA1), Ctrl (0x11/0xA2/0xA3) y Alt (0x12/0xA4/0xA5) a partir de los eventos. `Trigger` = tragar S (0x53) y disparar (el que llama inyecta el relleno). La S que se tragó abajo también se traga arriba; la repetición de S abajo se traga sin volver a disparar. Con `Selecting`, Esc (0x1B) abajo → `Cancel` y Esc arriba → `Swallow`. Los inyectados no disparan ni se tragan, pero los modificadores inyectados no cambian el estado.

- [ ] **Step 1: tests** — `WinShiftS_Triggers` (Win↓ Shift↓ S↓ → Trigger; S↑ → Swallow; Shift↑, Win↑ → Pass), `RepeatedS_SwallowedWithoutRetrigger`, `CtrlOrAlt_Pass` (Ctrl+Win+Shift+S y Alt+Win+Shift+S → Pass), `WithoutWin_Pass`, `Injected_Pass`, `RightWin_Triggers`, `Esc_CancelsOnlyWhileSelecting`, `ReleaseOrder_NoStuckState` (Shift↑ antes que S↑: S↑ igual se traga; luego S solo → Pass).
- [ ] **Step 2:** `dotnet test tests/Popete.Tests` → falla (no compila).
- [ ] **Step 3:** implementar `SnipKeys`.
- [ ] **Step 4:** tests pasan.
- [ ] **Step 5:** Commit.

### Task 3: Dónde va todo en el lugar (`InPlaceLayout`)

**Files:**
- Create: `src/InPlaceLayout.cs`, `tests/Popete.Tests/InPlaceLayoutTests.cs`

**Interfaces:**
- Produces: `static class InPlaceLayout` con `Rect Band(Rect capture, Rect monitor, double band)` (la captura agrandada `band` por lado, cortada al monitor) y `Point Bar(Rect band, Size bar, Rect work, double gap)` (debajo de la franja si entra en `work`; si no, arriba; si no, adentro, abajo de la captura; en X alineada a la izquierda de la franja y metida en `work`).

- [ ] **Step 1: tests** — `Band_InflatesAndClips` (captura en (0,0) → la franja empieza en 0), `Bar_BelowWhenFits`, `Bar_AboveWhenNoRoomBelow`, `Bar_InsideWhenNoRoom` (captura del monitor entero: la barra queda dentro de `work`), `Bar_ClampedHorizontally` (captura pegada a la derecha).
- [ ] **Step 2:** tests fallan. **Step 3:** implementar. **Step 4:** pasan. **Step 5:** Commit.

### Task 4: `EditorView` sin `EditorShell` (`IEditorHost`)

**Files:**
- Create: `src/IEditorHost.cs`
- Modify: `src/EditorView.cs`, `EditorView.AutoClose.cs`, `EditorView.Export.cs`, `EditorView.Recents.cs`, `EditorView.Drop.cs`, `EditorView.Toolbar.cs`, `src/EditorShell.cs`

**Interfaces:**
- Produces:
  ```csharp
  interface IEditorHost
  {
      Window Window { get; }                       // dueña de los diálogos; Activated/Deactivated para el cierre automático
      bool InPlace { get; }
      void Dismiss(EditorView view);               // salida rápida
      void Replace(EditorView view, Session session);
      void OpenInTab(EditorView view);             // EditorShell: no hace nada
  }
  ```
  `EditorView(IEditorHost host, Session session)`; campo `_owner` (mutable) y `public void MoveTo(IEditorHost host)`. `EditorShell : IEditorHost` y `public static void Adopt(EditorView view)` (abre la ventana si hace falta y agrega una pestaña con ese editor ya creado, elegida y `Engaged`).
- [ ] **Step 1:** reemplazar `_shell` por `_owner` (`_owner.Window` para diálogos y eventos).
- [ ] **Step 2:** `EditorShell.Adopt` (comparte con `Select` el alta del editor: `Saved` → miniatura, `_content`).
- [ ] **Step 3:** build sin advertencias; prueba manual 15 de AGENTS.md (pestañas) sigue igual.
- [ ] **Step 4:** Commit.

### Task 5: Modo «en el lugar» del editor

**Files:**
- Create: `src/EditorView.InPlace.cs`
- Modify: `src/EditorView.cs` (guardar `_toolbar`, `_statusBar`, `_canvasArea`, `_root`, `_limit`; `FitZoom`, `LayoutForImage`, teclado), `EditorView.IdeaCard.cs`, `EditorView.Pan.cs`, `EditorView.Toolbar.cs` (barra de estado sin zoom ni atajos en el lugar; botón «Abrir en pestaña»), `src/Theme.cs` (pincel `FranjaLugar`)

**Interfaces:**
- Consumes: `InPlaceLayout.Band/Bar` (Task 3), `IEditorHost` (Task 4).
- Produces: `public void ShowInPlace(Rect capture, Rect monitor, Rect work)` (en DIP de la ventana que lo aloja: arma la raíz `Canvas` con el lienzo en la franja y la barra flotante, zoom `1 / DpiScale`, sin sombra ni esquinas, borde de `Acento`, `_ideaOverlay` sobre toda la ventana); `public void UseTabLayout()` (vuelve al `DockPanel`); `bool InPlace`.
- Comportamiento en el lugar: Ctrl+rueda, Ctrl++/−/0, clic central y Ctrl+H → `_owner.OpenInTab(this)`; `FitZoom` solo fija `1 / DpiScale`; recortar reubica la franja (`LayoutForImage` → `LayoutInPlace`); clic derecho y el botón copian y cierran siempre; Ctrl+C según `CerrarAlCopiar`.
- [ ] **Step 1:** separar las partes del diseño en el constructor.
- [ ] **Step 2:** `ShowInPlace`/`UseTabLayout`/`LayoutInPlace`.
- [ ] **Step 3:** desvíos de zoom/pan/recientes y copiar.
- [ ] **Step 4:** build sin advertencias. **Step 5:** Commit.

### Task 6: La ventana en el lugar (`InPlaceWindow`)

**Files:**
- Create: `src/InPlaceWindow.cs`

**Interfaces:**
- Consumes: `Monitors` (Task 1), `IEditorHost`, `EditorView.ShowInPlace`, `EditorShell.Adopt`.
- Produces: `sealed class InPlaceWindow : Window, IEditorHost` con `static InPlaceWindow Current`, `static void Open(Session s, MonitorInfo monitor, Int32Rect capturePx, IntPtr previousForeground)`, `static void FinishCurrent()` (copia y cierra al instante, sin parpadeo).
- Ventana sin marco, `AllowsTransparency`, fondo transparente, `Topmost`, sin barra de tareas, del monitor entero (`PlaceInPixels`). Pérdida de foco (diferida a `ApplicationIdle`, salvo `IsActive`, `OwnedWindows.Count > 0` o `ComponentDispatcher.IsThreadModal`) → guardar la idea, copiar y cerrar. `Dismiss` cierra sin copiar y devuelve el foco a `previousForeground` si la ventana estaba activa. `OpenInTab` → `UseTabLayout`, `MoveTo(EditorShell)`, `EditorShell.Adopt`, cerrar la ventana sin `NotifyClosed`.
- [ ] **Step 1:** implementar. **Step 2:** build. **Step 3:** Commit.

### Task 7: Selección propia, hook y ajuste

**Files:**
- Create: `src/KeyboardHook.cs`, `src/ScreenSnip.cs`
- Modify: `src/AppSettings.cs` (`ModoTrabajo`, enum `ModoTrabajo { Pestanas, EnElLugar }`), `src/CaptureHost.cs` (instalar/quitar el hook en `Start`, `AppSettings.Changed` y la pausa; `HookFailed`), `src/SettingsWindow.cs` (fila arriba de General), `src/QuickHistoryWindow.cs` (`OnHotkey` llama primero a `InPlaceWindow.FinishCurrent`)

**Interfaces:**
- Consumes: `SnipKeys` (Task 2), `Monitors`, `InPlaceWindow.Open/FinishCurrent`, `History.Create`, `ClipboardHelper.Signature`.
- Produces: `sealed class KeyboardHook : IDisposable` (`KeyboardHook(Action onTrigger, Action onCancel)`, `bool Installed`, `bool Selecting { set; }`; hilo de fondo con `GetMessage`; inyecta VK 0xE8 abajo/arriba en `Trigger`; publica en el dispatcher con `BeginInvoke`). `static class ScreenSnip` con `bool Active`, `void Start()`, `void Cancel()`.
- `ScreenSnip.Start`: `FinishCurrent`, foto de `Monitors.VirtualScreen()` (`Graphics.CopyFromScreen` con `CaptureBlt`) antes de mostrar nada, una ventana opaca por monitor con su parte, oscurecida (60 % negro) salvo el recuadro, borde de 1 px blanco, cursor en cruz. Al soltar (≥ 5 px por lado, dentro del monitor de origen): `History.Create` con la huella, `InPlaceWindow.Open` y recién entonces cerrar las ventanas de la selección (sin parpadeo). Esc (teclado o hook) y clic derecho cancelan.
- [ ] **Step 1:** `KeyboardHook`. **Step 2:** `ScreenSnip`. **Step 3:** ajuste, `CaptureHost`, Ajustes y Alt+`.
- [ ] **Step 4:** build y tests; prueba manual: modo «En el lugar», Win+Shift+S → recuadro → anotar → clic afuera → pegar en Paint; Esc; «Abrir en pestaña» conserva Ctrl+Z; volver a «En pestañas» → Recortes.
- [ ] **Step 5:** Commit.

### Task 8: Documentación

**Files:** `AGENTS.md` (filas de arquitectura nuevas, nota 53, pruebas manuales 25–26, DPI del sistema → PerMonitorV2 en las notas que lo mencionan), estado del diseño.

- [ ] **Step 1:** actualizar. **Step 2:** Commit.
