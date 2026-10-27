# Ideas por número — plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada número de una captura puede llevar una idea (texto que no sale en la imagen) que viaja con la imagen al copiarla, para pasársela a una IA.

**Architecture:** La lógica pura (qué número lleva cada elemento, número fantasma, formato del texto) va en `src/Ideas.cs`, sin WPF, con tests xUnit. La idea vive en el elemento: propiedad ISF en el trazo y `Idea` en `NoteText`/`TextNote`. Todo lo del editor (campo, marca, fantasma, lista) va en `src/EditorView.Ideas.cs`, en una capa nueva `_ideaLayer` que está en `_board` pero fuera de `_surface`, así nunca se exporta. Cada guardado escribe `ideas.txt` en la carpeta de la captura; de ahí lo lee el historial rápido.

**Tech Stack:** C# / .NET 10, WPF + WinForms (`net10.0-windows10.0.19041.0`), xUnit para el proyecto de tests nuevo.

**Spec:** `docs/superpowers/specs/2026-10-26-ideas-por-numero-design.md` (leerlo antes de cada tarea).

## Global Constraints

- Textos de UI y comentarios en español. Un archivo por área: lo nuevo del editor va en `EditorView.Ideas.cs`.
- Colores de la UI siempre por recurso (`SetResourceReference`), nunca un pincel fijo (nota 11 de AGENTS.md). Los colores de anotaciones/números sí son fijos.
- WinForms y System.Drawing con nombre completo, sin `using`. Sin `System.Windows.Shapes` importado (nota 1).
- Nada de `MessageBox` (nota 31). Teclas del editor por `PreviewKeyDown` (nota 3). Botones de barra `Focusable=False` (nota 13).
- El portapapeles se escribe solo desde `ClipboardHelper`, y la huella se registra antes (nota 4).
- Nada de lo nuevo puede salir en `anotada.png` ni en lo copiado, salvo los números de las ideas.
- Una captura **sin** ideas se copia exactamente como hoy (mismo `DataObject`).
- Encabezado de fábrica, literal: `En la imagen, cada número marca un punto:`
- Verificar con `dotnet build src` sin errores ni advertencias (cerrar Popete antes desde la bandeja: si no, el .exe está bloqueado). Tests: `dotnet test tests/Popete.Tests`.
- **Antes de empezar:** hay cambios sin commitear del usuario en `AGENTS.md`, `src/CaptureHost.cs` y `src/ClipboardHelper.cs` (filtro de la nota 47). No mezclarlos con commits de este plan: trabajar en un worktree/rama creado después de que el usuario los commitee, o preguntarle.

## Review Focus

- **Teclas con el campo vacío recién abierto:** Tab, Supr/Retroceso, Ctrl+Z, Ctrl+C, flechas y Esc tienen que actuar en el editor (y Esc no debe cerrar la pestaña). Una letra escribe. → prueba manual en la tarea 3.
- **Deshacer mezclado:** Ctrl+Z después de escribir una idea deshace la idea (el trazo sigue); otro Ctrl+Z deshace el trazo. Con el campo con texto, Ctrl+Z edita el texto. → prueba manual en la tarea 3.
- **Capturas viejas:** reabrir una captura de antes no cambia ningún número (flechas sin `FreeArrowProperty` = unidas) y no crea `ideas.txt`. → test de `Assign` en la tarea 1 y prueba manual en la tarea 2.
- **Ideas en blanco o de varias líneas:** solo espacios = sin idea; varias líneas → sangría; saltos de línea al final no dejan líneas vacías. → tests de `Ideas` en la tarea 1.
- **Campo abierto al salir:** cambiar de pestaña, cerrar, copiar o el guardado automático no pierden la idea, y el campo no sale en `anotada.png`. → prueba manual en la tarea 3.

---

### Task 0: Prueba desechable del portapapeles (PNG + texto)

Solo condiciona la tarea 6. Las tareas 1–5 pueden avanzar mientras tanto.

**Files:**
- Create (no se commitea): `<scratchpad>\prueba-portapapeles.ps1`

- [ ] **Step 1: Escribir el script.** Parámetro `-Png <ruta>`. Corre con `powershell -STA -File`. Arma un `System.Windows.Forms.DataObject` con el formato `"PNG"` (un `MemoryStream` con los bytes del archivo), `SetImage` (un `System.Drawing.Bitmap` del mismo archivo) y `SetText` con este texto, en `UnicodeText`:

  ```
  En la imagen, cada número marca un punto:

  1. Prueba de idea uno.
  2. Prueba de idea dos.
     Segunda línea.
  ```

  Después llama a `System.Windows.Forms.Clipboard.SetDataObject($data, $true)`. Si Popete está abierto, ignora esta imagen porque también lleva texto (nota 47).
- [ ] **Step 2: Correrlo** con un `anotada.png` del historial y pedirle al usuario que pegue en: claude.ai, ChatGPT, Claude Code en la terminal (probar Ctrl+V y Alt+V) y Claude Code en VS Code. Para cada uno, anotar qué llega: imagen, texto o las dos.
- [ ] **Step 3: Anotar el resultado** en la sección «Resultado de la tarea 0» al final de este plan y commitear solo el plan. Si en algún destino no llegan las dos cosas, **no hacer la tarea 6**: avisar al usuario y frenar ahí.

---

### Task 1: Lógica pura de las ideas y proyecto de tests

**Files:**
- Create: `src/Ideas.cs`
- Create: `tests/Popete.Tests/Popete.Tests.csproj`, `tests/Popete.Tests/IdeasTests.cs`
- Modify: `src/Popete.csproj` (agregar `<InternalsVisibleTo Include="Popete.Tests" />` en un `ItemGroup`)

**Interfaces:**
- Produces:
  - `readonly record struct NumberCandidate(long Order, bool HasIdea)`
  - `static class Ideas` con:
    - `const string DefaultHeader = "En la imagen, cada número marca un punto:"`
    - `static bool HasText(string idea)`: no es null ni solo espacios.
    - `static int[] Assign(IReadOnlyList<NumberCandidate> items, bool numberAll)`: número de cada candidato, en el mismo índice; 0 = sin número. Con `numberAll`, todos 1..n por `Order` ascendente. Sin `numberAll`, solo los `HasIdea`, 1..k por `Order`. Empates por índice.
    - `static int GhostNumber(IReadOnlyList<NumberCandidate> items, int index)`: el número que tomaría `items[index]` si tuviera idea, con `numberAll: false`.
    - `static string List(IEnumerable<(int number, string idea)> items)`: líneas separadas por `"\r\n"`, ordenadas por número; omite las que no tienen `HasText`. `Trim()` a cada idea; las líneas siguientes de una idea llevan como sangría tantos espacios como `"N. "`, sin espacios al final de la línea. Devuelve `""` si no queda nada.
    - `static string WithHeader(string header, string list)`: `null` si `list` está vacía; solo `list` si `header` no tiene texto; si no, `header.Trim() + "\r\n\r\n" + list`.

- [ ] **Step 1: Crear el proyecto de tests.** xUnit, `TargetFramework` `net10.0-windows10.0.19041.0`, `UseWPF` true, `ProjectReference` a `..\..\src\Popete.csproj`. Va fuera de `src/` para que `Popete.csproj` no recoja sus `.cs`.
- [ ] **Step 2: Escribir los tests que fallan** en `IdeasTests.cs`:

```csharp
[Fact] public void Assign_todo_numera_por_orden()
    => Assert.Equal(new[] { 2, 1, 3 }, Ideas.Assign(new[] { new NumberCandidate(5, false), new NumberCandidate(2, true), new NumberCandidate(9, false) }, numberAll: true));

[Fact] public void Assign_sin_todo_numera_seguido_solo_las_ideas()
    => Assert.Equal(new[] { 0, 1, 0, 2 }, Ideas.Assign(new[] { new NumberCandidate(1, false), new NumberCandidate(2, true), new NumberCandidate(3, false), new NumberCandidate(4, true) }, numberAll: false));

[Fact] public void GhostNumber_es_el_lugar_entre_las_ideas()
    => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, true), new NumberCandidate(2, false), new NumberCandidate(3, true) }, 1));

[Fact] public void HasText_blanco_no_es_idea()
{ Assert.False(Ideas.HasText(null)); Assert.False(Ideas.HasText("  \r\n ")); Assert.True(Ideas.HasText(" a ")); }

[Fact] public void List_salta_vacias_y_ordena()
    => Assert.Equal("1. Uno\r\n3. Tres", Ideas.List(new[] { (3, "Tres"), (2, "   "), (1, " Uno ") }));

[Fact] public void List_sangra_las_lineas_siguientes()
    => Assert.Equal("3. Este botón debería decir «Guardar».\r\n   Y estar a la derecha.",
        Ideas.List(new[] { (3, "Este botón debería decir «Guardar».\nY estar a la derecha.\r\n\r\n") }));

[Fact] public void List_sangra_numeros_de_dos_cifras()
    => Assert.Equal("12. a\r\n    b", Ideas.List(new[] { (12, "a\nb") }));

[Fact] public void WithHeader_casos()
{
    Assert.Null(Ideas.WithHeader(Ideas.DefaultHeader, ""));
    Assert.Equal("1. a", Ideas.WithHeader("  ", "1. a"));
    Assert.Equal("En la imagen, cada número marca un punto:\r\n\r\n1. a", Ideas.WithHeader(Ideas.DefaultHeader, "1. a"));
}
```

- [ ] **Step 3: Correr los tests y ver que fallan.** Comando: `dotnet test tests/Popete.Tests`. Esperado: falla de compilación, `Ideas`/`NumberCandidate` no existen.
- [ ] **Step 4: Implementar `src/Ideas.cs`** con las firmas de arriba y un encabezado de comentario como los de los demás archivos.
- [ ] **Step 5: Correr `dotnet test tests/Popete.Tests`.** Esperado: los 8 tests pasan. `dotnet build src` sin advertencias.
- [ ] **Step 6: Commit.** Mensaje: `Ideas: lógica de numeración y formato del texto, con tests`. Agregar en AGENTS.md la fila `Ideas.cs` a la tabla de arquitectura y, en «Comandos», `dotnet test tests/Popete.Tests`. En «Estado actual», cambiar «no hay tests automatizados» por «solo la lógica de `Ideas`».

---

### Task 2: Guardar la idea y la numeración nueva (flechas libres, «solo lo que tiene idea»)

**Files:**
- Create: `src/EditorView.Ideas.cs` (propiedades y accesores; las tareas siguientes lo amplían)
- Modify: `src/EditorView.Numbers.cs` (`RefreshNumbers`, comentario de cabecera, `IsArrow` → uso de `IsTiedArrow`)
- Modify: `src/EditorView.Drawing.cs:43-45` (marcar flechas libres)
- Modify: `src/NoteText.cs` (propiedad `Idea`), `src/History.cs` (`TextNote.Idea`, `Session.IdeasPath`)
- Modify: `src/EditorView.cs` (`LoadAnnotations` lee `Idea`; `SaveSession` la escribe y escribe `ideas.txt`)
- Modify: `src/EditorView.Toolbar.cs:82-84`, `src/SettingsWindow.cs:42-44` (textos del interruptor)

**Interfaces:**
- Consumes: `Ideas.Assign`, `Ideas.HasText`, `Ideas.List`, `NumberCandidate` (tarea 1).
- Produces (en `EditorView`):
  - `static readonly Guid IdeaProperty`, `static readonly Guid FreeArrowProperty`: GUIDs nuevos, generarlos.
  - `static bool IsTiedArrow(Stroke s)` = `IsArrow(s) && !s.ContainsPropertyData(FreeArrowProperty)`.
  - `static string IdeaOf(object target)` (Stroke → `IdeaProperty` como string; `NoteText` → `.Idea`; otro → null).
  - `static void SetIdeaRaw(object target, string idea)`: sin `HasText`, quita la propiedad / pone null.
  - `List<(object target, int number, Point center, Color color)> Numbering()`: los elementos con número, en orden. `RefreshNumbers` dibuja desde aquí.
  - `Point AutoNumberCenter(object target)`: centro del círculo de ese elemento (el cálculo actual de la línea 128, offset incluido), tenga número o no.
  - `string IdeasList()` = `Ideas.List` de `Numbering()` con `IdeaOf`.
  - `NoteText.Idea` (string), `TextNote.Idea` (string, null si no hay), `Session.IdeasPath` = `Folder\ideas.txt`.

- [ ] **Step 1: Flechas libres.** En `Ink_MouseDown`, cuando `_tool == Tool.Arrow` y `StrokeAt(_start)` es null, agregar `FreeArrowProperty` (`true`) al `_preview`, además de `ArrowProperty`. Actualizar el comentario «las flechas no se numeran».
- [ ] **Step 2: Numeración.** `Numbering()` arma los candidatos: todos los trazos menos `_preview`, los que no tienen puntos y los `IsTiedArrow`, más todos los textos. Para cada uno, `new NumberCandidate(orden, Ideas.HasText(IdeaOf(t)))`, y llama a `Ideas.Assign(cands, NumbersOn)`. `RefreshNumbers` ya no corta en `if (!NumbersOn) return;`: dibuja lo que devuelva `Numbering()`. Reescribir el comentario de cabecera de `EditorView.Numbers.cs` con la regla nueva.
- [ ] **Step 3: Persistencia.** `TextNote.Idea` va al guardar y al cargar (`AddTextBlock` → `NoteText.Idea`). En `SaveSession`, después de `textos.json`, se escribe `IdeasList()`: si no está vacía, en `_session.IdeasPath` con `History.WriteAtomic` (UTF-8); si está vacía y el archivo existe, se borra.
- [ ] **Step 4: Textos del interruptor.** Botón: tooltip `Numerar todo (N) · apagado: solo lo que tiene idea` y `AutomationProperties.Name` `Numerar todo`. Ajustes: etiqueta `Numerar todo en capturas nuevas` y ayuda `Apagado, solo se numera lo que tiene idea. Cada captura puede cambiarlo con el botón «123» de la barra.`
- [ ] **Step 5: Verificar.** `dotnet build src` sin advertencias. A mano:
  - Una flecha que empieza en el vacío lleva número; una que sale de un trazo, no.
  - Abrir una captura vieja con flechas: los números no cambian y no aparece `ideas.txt` en su carpeta.
  - Con la numeración apagada no se ve ningún número, como hoy.
- [ ] **Step 6: Commit.** Mensaje: `Ideas: guardar la idea en trazos y textos; flechas libres numeradas`. En AGENTS.md, la fila de `EditorView.Numbers.cs` y una nota nueva en «Decisiones»: por qué se marcan las flechas **libres** y no las unidas (las viejas quedan sin número sin migrar).

---

### Task 3: El campo de idea, la marca y el tooltip

**Files:**
- Modify: `src/EditorView.Ideas.cs`, `src/EditorView.cs` (capa en `_board`, `LayoutForImage`, `OnKeyDown`, clic derecho), `src/EditorView.Drawing.cs` (abrir tras el trazo, `CommitText`, `Ink_MouseDown`), `src/EditorView.Numbers.cs` (`NumberUp`, tooltip y marca), `src/EditorView.Export.cs` (nada si `CommitText` ya cierra el campo: comprobarlo)

**Interfaces:**
- Consumes: `IdeaOf`, `SetIdeaRaw`, `AutoNumberCenter`, `IsTiedArrow`, `Numbering()` (tarea 2).
- Produces:
  - `Canvas _ideaLayer` (sin fondo; hijo de `_board` justo después de `_surface`; en la lista de tamaños de `LayoutForImage`).
  - `TextBox _ideaBox`, `object _ideaTarget`.
  - `void OpenIdea(object target)`: cierra el campo anterior y abre uno junto a `AutoNumberCenter(target)`, sin tapar el número, con el foco puesto (`Dispatcher.InvokeAsync(... Focus, DispatcherPriority.Input)`, como `BeginText`). Si ya tiene idea, la carga con el cursor al final. `AcceptsReturn = true`. Llama a `MarkTouched()`.
  - `void CommitIdea()`: idempotente. Si cambió el texto, `ChangeIdea(_ideaTarget, texto)`. Saca el campo. Si el elemento ya no existe (trazo borrado), no hace nada.
  - `void ChangeIdea(object target, string after)`: `SetIdeaRaw` + `PushUndo(new ActionChange(...))` con el antes y el después. No hace nada si los dos son iguales según `HasText`/texto.
  - `bool IdeaKey(KeyEventArgs e)`: true = el campo se quedó con la tecla.

- [ ] **Step 1: Capa y campo.** `_ideaLayer` y `OpenIdea`/`CommitIdea`/`ChangeIdea` como arriba. `LostKeyboardFocus` del campo → `CommitIdea()`. `CommitText()` empieza llamando a `CommitIdea()`: así lo cierran todos los caminos que hoy terminan un texto (cambiar herramienta, deshacer, copiar, cambiar de pestaña, `PrepareClose`, clic en un número). `Ink_MouseDown` llama a `CommitIdea()` antes que nada.
- [ ] **Step 2: Abrir tras cada trazo.** `void OfferIdea(Stroke s)`: si `!IsTiedArrow(s)`, llama a `OpenIdea(s)`. Se llama al final del `StrokeCollected` (después de `AdvanceColor`) y en la rama que acepta la forma en `Ink_MouseUp`. Después de un texto no se llama.
- [ ] **Step 3: Teclas.** Al principio de `OnKeyDown`: `if (_ideaBox != null && IdeaKey(e)) return;`. Reglas de `IdeaKey`:
  - **Enter sin Shift:** `CommitIdea()`, manejada.
  - **Shift+Enter:** no manejada por el editor (salto de línea del `TextBox`).
  - **Esc:** `CommitIdea()`, manejada (no llega a `Dismiss`).
  - **Campo vacío y tecla Tab, Supr, Retroceso, una flecha, o con Ctrl/Alt:** `CommitIdea()` y devuelve false (el editor actúa con esa tecla).
  - **Cualquier otra:** devuelve true sin manejarla (el `TextBox` escribe). Con texto en el campo, Ctrl+Z/Ctrl+C/Supr son del `TextBox`, como con `_editing`.
- [ ] **Step 4: Clic derecho.** En `PreviewMouseRightButtonUp`, salir también si `_ideaBox` tiene texto (como con `_editing`). Con el campo vacío, copia como siempre (`CopyToClipboard` → `CommitText` lo cierra).
- [ ] **Step 5: Marca, tooltip y clic en el número.** En `RefreshNumbers`, el círculo de un elemento con idea lleva de `ToolTip` la idea. Los demás conservan el texto de hoy. Además, en `_ideaLayer`, una marca chica sobre su borde superior derecho, en el color de recurso `Acento` con borde blanco, en un `Canvas` hijo propio (`_ideaMarks`) que se vacía en cada refresco sin tocar `_ideaBox`. En `NumberUp`, en la rama del clic sin arrastre, después de `DragEnd()`: `OpenIdea(d.target)`.
- [ ] **Step 6: Verificar.** `dotnet build src`. A mano:
  - **Campo:** dibujar → campo con foco; escribir «hola», Enter → marca y tooltip; clic en el número → reabre con «hola».
  - **Teclas con el campo vacío:** Tab cambia a flecha, Supr borra el trazo, Ctrl+Z lo deshace, Esc no cierra la pestaña, Ctrl+C copia. «a» escribe.
  - **Deshacer:** idea escrita → Ctrl+Z la quita y el trazo queda; otro Ctrl+Z quita el trazo.
  - **Salir con el campo abierto:** escribir sin Enter y cambiar de pestaña, o copiar y cerrar → la idea quedó. `anotada.png` no muestra ni el campo ni la marca.
  - **Flecha unida:** no abre campo.
  - **Numeración apagada:** dibujar, escribir, Enter → aparece su número (1); otro trazo sin idea → sin número.
- [ ] **Step 7: Commit.** Mensaje: `Ideas: campo junto al número, marca y tooltip`. En AGENTS.md, la fila de `EditorView.Ideas.cs` y una nota en «Decisiones»: `_ideaLayer` fuera de `_surface`, `CommitText` cierra el campo y las reglas de teclas.

---

### Task 4: Número fantasma

**Files:**
- Modify: `src/EditorView.Ideas.cs`, `src/EditorView.Selection.cs` (refrescar al cambiar la selección), `src/EditorView.Numbers.cs` (refrescar en `RefreshNumbers`)

**Interfaces:**
- Consumes: `Ideas.GhostNumber`, `Numbering()`, `AutoNumberCenter`, `NumberBadge`, `OpenIdea`.
- Produces: `void RefreshGhost()`.

- [ ] **Step 1: Implementar `RefreshGhost()`.** Hay un objetivo si la selección es exactamente un elemento (un trazo o un texto), o si no la hay pero `_ideaTarget` está puesto. Se descarta si es un `IsTiedArrow` o si ya está en `Numbering()`. Con objetivo, se dibuja en `_ideaLayer` un `NumberBadge(Ideas.GhostNumber(...), color, NumberRadius)` con opacidad baja (unos 0,45) en `AutoNumberCenter`. Un clic abre `OpenIdea(objetivo)` y se marca manejado. Si no hay objetivo, se quita. Se llama al final de `Select(...)` en `EditorView.Selection.cs`, al final de `RefreshNumbers`, en `OpenIdea` y en `CommitIdea`.
- [ ] **Step 2: Verificar.** `dotnet build src`. A mano, con la numeración apagada:
  - Dibujar y cerrar el campo vacío → sin número.
  - Doble clic en ese trazo → fantasma tenue con el número que tomaría; clic → campo; escribir y Enter → número real.
  - Soltar la selección → el fantasma desaparece.
  - Copiar → el fantasma no sale en la imagen.
- [ ] **Step 3: Commit.** Mensaje: `Ideas: número fantasma para anotar lo que no tiene número`.

---

### Task 5: Lista «N ideas», Copiar texto y encabezado en Ajustes

**Files:**
- Modify: `src/EditorView.Ideas.cs`, `src/EditorView.Toolbar.cs` (`BuildStatusBar`), `src/ClipboardHelper.cs`, `src/AppSettings.cs`, `src/SettingsWindow.cs`

**Interfaces:**
- Consumes: `Numbering()`, `IdeaOf`, `IdeasList()`, `OpenIdea`, `Ideas.WithHeader`.
- Produces:
  - `string AppSettings.EncabezadoIdeas { get; set; } = Ideas.DefaultHeader;`
  - `static bool ClipboardHelper.SetText(string text)`: con los mismos 5 reintentos de `SetPng`. Extraer el bucle a `static bool TrySet(DataObject data)` y que lo usen los dos.
  - `string IdeasText()` = `Ideas.WithHeader(AppSettings.Current.EncabezadoIdeas, IdeasList())`.
  - `void RefreshIdeasButton()`: se llama desde `RefreshNumbers`.

- [ ] **Step 1: Indicador.** Un botón `EstiloPlano` en la barra de estado, a la izquierda de la zona del zoom: «1 idea» / «N ideas», `Collapsed` con 0. Al hacer clic abre un `ContextMenu` (toma el estilo implícito del tema, nota 25) con:
  - un `MenuItem` por idea: «N. primera línea», recortada a unos 60 caracteres;
  - un `Separator`;
  - «Copiar texto».

  Clic en una idea: `BringIntoView()` del círculo de ese número y `OpenIdea(target)`. «Copiar texto»: `ClipboardHelper.SetText(IdeasText())`, y `_status.Text` = `✓ Ideas copiadas al portapapeles` (o el mismo aviso de error que en `CopyToClipboard`).
- [ ] **Step 2: Ajuste.** En Ajustes › Editor, debajo de la fila de numeración, una fila «Encabezado de las ideas» con un `TextBox` de una línea, colores por recurso (`Superficie`, `Texto`, `Borde`). Se guarda con `Save(...)` al perder el foco y al cerrar la ventana. Ayuda: `Va antes de la lista al copiar. Vacío: solo la lista.`
- [ ] **Step 3: Verificar.** `dotnet build src`. A mano:
  - Tres trazos, ideas en el 1 y el 3 → «2 ideas»; la lista muestra 1 y 3; clic en el 3 → abre su campo.
  - «Copiar texto» y pegar en el Bloc de notas → encabezado de fábrica, línea en blanco, `1. …`, `3. …`. Popete no abre un editor.
  - Cambiar el encabezado en Ajustes → la próxima copia lo usa; vacío → solo la lista.
- [ ] **Step 4: Commit.** Mensaje: `Ideas: lista en la barra de estado, Copiar texto y encabezado en Ajustes`. Actualizar en AGENTS.md la fila de `AppSettings.cs` y la de `ClipboardHelper.cs`.

---

### Task 6: Pegado conjunto en Copiar y en Alt+` (solo si la tarea 0 salió bien)

**Files:**
- Modify: `src/ClipboardHelper.cs` (`SetPng`), `src/EditorView.Export.cs` (`CopyToClipboard`), `src/QuickHistoryWindow.cs` (`PasteSelected`)

**Interfaces:**
- Consumes: `IdeasText()` (tarea 5), `Session.IdeasPath` (tarea 2), `Ideas.WithHeader`, `TrySet` (tarea 5).
- Produces: `static bool ClipboardHelper.SetPng(byte[] png, Session session, string text = null)`. Con `text` null, arma el mismo `DataObject` que hoy; si no, además `data.SetData(DataFormats.UnicodeText, text)`.

- [ ] **Step 1: `SetPng` con texto,** como arriba. La huella se sigue registrando primero.
- [ ] **Step 2: Editor.** `CopyToClipboard` pasa `IdeasText()`, que es null sin ideas. Así lo usan Ctrl+C, Ctrl+Enter, el clic derecho y el botón.
- [ ] **Step 3: Historial rápido.** `PasteSelected` lee `s.IdeasPath` si existe (`try`, como la imagen) y pasa `Ideas.WithHeader(AppSettings.Current.EncabezadoIdeas, contenido)`.
- [ ] **Step 4: Verificar.** `dotnet build src`. A mano:
  - Una captura con ideas → Ctrl+C → pegar en claude.ai: llegan la imagen y el texto.
  - Una sin ideas → Ctrl+C → pegar en Word y en Paint: la imagen, como hoy.
  - Alt+` sobre la de las ideas, soltar en claude.ai → las dos cosas.
  - Después de copiar, Popete no abre un editor nuevo.
- [ ] **Step 5: Commit y documentación.** Mensaje: `Ideas: copiar y Alt+\` llevan el texto junto a la imagen`. En AGENTS.md:
  - una nota en «Decisiones»: texto en el portapapeles solo si hay ideas, y por qué;
  - la prueba manual **21** (o la que siga) con el recorrido completo de las tareas 2 a 6;
  - en «Estado actual», las ideas como pendiente de probar a mano.

---

## Resultado de la tarea 0

(Lo completa la tarea 0: destino → qué llegó.)
