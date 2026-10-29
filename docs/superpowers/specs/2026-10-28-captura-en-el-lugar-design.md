# Captura en el lugar — diseño

Fecha: 2026-10-28. Estado: acordado con el usuario en conversación; implementado (plan `docs/superpowers/plans/2026-10-28-captura-en-el-lugar.md`), pendiente de prueba manual.

## Para qué

Hoy, después de Win+Shift+S, Popete abre la captura en la ventana del editor: en otro lugar de la pantalla y en otro tamaño (ajustada, por ejemplo al 62 %). El usuario tiene que **buscarla con la vista** cada vez. La funcionalidad deja anotar la captura **exactamente donde estaba en la pantalla, a tamaño real**: la imagen no salta ni cambia de tamaño, y el resto de la pantalla sigue a la vista.

Recortes de Windows no dice dónde se tomó la captura (el portapapeles solo trae `Bitmap` y `PNG`), así que Popete hace **su propia captura** cuando se aprieta Win+Shift+S. Así sabe el rectángulo exacto.

## Objetivos

- Win+Shift+S (el mismo atajo de siempre) abre la selección de Popete; al soltar el recuadro, ese recuadro **se convierte** en el editor, en el mismo lugar y a 1 píxel de pantalla por píxel de captura.
- El editor en el lugar tiene **todo** lo que tiene el editor de pestañas (herramientas, colores, grosores, deshacer, ideas por número, Ctrl+C con el texto de las ideas, atajos, recortar), salvo lo que rompe la continuidad (zoom, desplazamiento) o es de la ventana (pestañas, panel de recientes).
- Exacto en cualquier monitor, incluso con escalas distintas: la app pasa a **PerMonitorV2**.
- Todo esto es un **modo de trabajo nuevo, apagado de fábrica**. Con el modo de hoy no cambia nada.

## No objetivos

- Modos de selección ventana, pantalla completa y forma libre: **solo rectángulo** (es el único que usa el usuario).
- Ubicar en su lugar las capturas que llegan por el portapapeles (Impr Pant, Recortes, otros programas): siguen abriendo en pestaña, como hoy.
- Varias capturas en el lugar a la vez: hay una sola como mucho.
- Zoom dentro del editor en el lugar.

## Anti-objetivos (aunque funcione, sería un fracaso)

- Que la imagen aparezca corrida, aunque sea un píxel, o reescalada o borrosa respecto de lo que había en la pantalla.
- Que el teclado de todo el sistema se trabe o se demore porque Popete está ocupado (el hook de teclado responde siempre al instante).
- Que al usar Win+Shift+S se abra el menú Inicio, o que se abra también Recortes.
- Que Win+Shift+S deje de funcionar: si Popete no puede tomarla (cerrado, colgado, en pausa, ventana de administrador al frente), tiene que llegar a Windows como siempre.
- Que quien no activó el modo nuevo note cualquier diferencia en la captura (ni hook instalado, ni Win+Shift+S cambiado). El paso a PerMonitorV2 sí es para todos (ver «Escalas»): en monitores con otra escala Popete se ve nítido en lugar de estirado, y eso es una mejora buscada.
- Que el editor en el lugar tape el contexto de alrededor más que una franja angosta.

## Comportamiento

### El ajuste: modo de trabajo

- Ajustes › General, una fila nueva arriba: **modo de trabajo** con dos valores, «En pestañas» (de fábrica; Win+Shift+S es de Recortes, como hoy) y «En el lugar» (Popete captura con Win+Shift+S y se anota sobre la pantalla). El texto exacto queda al builder. *(Ubicación en General: decisión del agente.)*
- Nuevo campo de `AppSettings` (por ejemplo `ModoTrabajo`, enum; de fábrica, pestañas). Cambiarlo instala o quita el hook en caliente (`AppSettings.Changed`).
- Con la detección en pausa (bandeja), el hook también se suelta: Win+Shift+S vuelve a ser de Recortes mientras dure la pausa. *(Decisión del agente.)*
- `novedades.txt` de la próxima versión lo menciona (lo escribe el usuario o el agente al publicar; no es parte de este trabajo).

### Tomar Win+Shift+S

- `RegisterHotKey` no sirve (Windows ya registró la combinación). Se usa un hook de teclado de bajo nivel (`WH_KEYBOARD_LL`), **en un hilo propio con su bucle de mensajes**, nunca en el hilo de WPF: la callback solo decide y, si es la combinación, la publica en el dispatcher (`BeginInvoke`) y devuelve enseguida.
- Combinación: S apretada mientras están apretadas Win (izquierda o derecha) y Shift, sin Ctrl ni Alt. Se tragan la S abajo y la S arriba. Para que al soltar Win no se abra Inicio, se inyecta una tecla de relleno sin asignar (como hace PowerToys; por ejemplo VK 0xE8) antes de que Win se suelte. Los eventos inyectados (`LLKHF_INJECTED`) se ignoran.
- La lógica de decisión (qué teclas se tragan, cuándo inyectar el relleno) va separada de la P/Invoke y con tests, como `IdeaKeys`.
- Si una ventana de administrador está al frente, Windows no le pasa las teclas al hook: Win+Shift+S abre Recortes y la captura llega por el portapapeles a una pestaña, como hoy. Es el respaldo esperado, no un error.
- Si el hook no se pudo instalar, se anota con `AppInfo.LogError` y Ajustes muestra el aviso en la fila del modo (como `HotkeyFailed`). *(Decisión del agente.)*

### La selección

- Al tomar la combinación: se toma una foto de todos los monitores (en píxeles físicos) **antes** de mostrar nada, y se cubre cada monitor con una ventana sin marco, encima de todo, fuera de la barra de tareas, con su foto congelada y oscurecida. Cursor en cruz.
- Arrastrar dibuja el recuadro, que se ve iluminado (sin oscurecer) con un borde fino; al soltar, ese recuadro pasa a ser el editor en el lugar. Es lo mismo que hace Recortes en modo rectángulo.
- El recuadro queda dentro del monitor donde empezó el arrastre. *(Decisión del agente: simplifica el DPI.)*
- Esc o clic derecho cancelan: no se guarda nada. Un clic sin arrastrar (recuadro de menos de 5 px por lado) no hace nada y sigue la selección. *(Decisión del agente.)*
- La imagen es el recorte de la foto congelada (no una foto nueva): es lo que el usuario vio al elegir.
- Al soltar, la captura se guarda en el historial (`History.Create` con su huella) como cualquier captura nueva, y su huella queda en `History.Known`.
- Si ya hay una captura en el lugar abierta, Win+Shift+S cuenta como un clic afuera (copia y cierra esa) antes de tomar la foto, que no debe salir con el editor anterior encima.

### El editor en el lugar

- La captura se ve en el mismo rectángulo de la pantalla donde estaba, a 1 píxel físico por píxel de captura, sin redondeo de esquinas ni sombra sobre la imagen que la corra o la tape. Un borde fino de `Acento` por fuera la distingue de la pantalla en vivo.
- Alrededor, una **franja clara** angosta (del orden de 40–60 px; el valor exacto queda al builder) donde también se puede dibujar: el margen de hoy, pero mucho más chico; lo que quede ahí sale con fondo blanco al copiar, como hoy. La franja se corta en los bordes del monitor. El resto de la pantalla es la **pantalla en vivo**, sin tocar.
- **Barra flotante** con las herramientas del editor (todo lo de la barra de hoy y lo útil de la barra de estado: «N ideas», quitar recorte, Ajustes, estado de guardado) más un botón **«Abrir en pestaña»**. Va debajo de la franja si entra en el área de trabajo del monitor, si no arriba, y como último recurso adentro, abajo de la captura. Si la barra de hoy no entra a lo ancho, el builder la compacta o la parte en dos filas. La lógica de dónde va, sin WPF y con tests (como `IdeaCardPlacement`).
- La tarjeta de las ideas tiene que poder abrirse aunque la captura sea chica: puede salir de la franja hacia la pantalla.
- Funciona igual que el editor de hoy en todo lo demás: teclado (P, flechas, 1–3, Ctrl+Z…), selección, asas, números, ideas, recortar (el marco sobre la captura en su lugar; al aplicar, lo recortado se va y lo que queda sigue en el mismo lugar, porque la tabla está anclada a la original, nota 21), guardado automático, Ctrl+S.
- No hay zoom ni desplazamiento: Ctrl+rueda, Ctrl++/−, Ctrl+0 y el clic central **pasan la captura a una pestaña** (la acción empieza en la pestaña, no se repite). *(Acordado al repasar el diseño; el usuario no lo objetó.)* Ctrl+H (recientes) también pasa a pestaña. *(Decisión del agente.)*

### Terminar

| Acción | Resultado |
|---|---|
| Clic fuera de la franja y de la barra (cae en el programa de abajo), o cualquier otra pérdida de foco hacia otro programa (Alt+Tab, Win) | **Copiar y cerrar**, siempre, tocada o no (con el texto de las ideas si las hay). Una idea a medio escribir se guarda antes, como Ctrl+Enter. |
| Ctrl+Enter, clic derecho sobre la captura, botón «Copiar y cerrar» | Copiar y cerrar. |
| Ctrl+C | Copia; cierra o no según `CerrarAlCopiar`, como hoy. *(Decisión del agente.)* |
| Esc | Cierra sin copiar (como la salida rápida de hoy); con un texto o una idea abiertos, primero los termina, como hoy. |
| «Abrir en pestaña» (o zoom, pan, Ctrl+H) | Se cierra el editor en el lugar y la captura se abre en una pestaña de `EditorShell` con todo lo hecho (se guardó antes). Ahí sí cambia de lugar: es lo esperado. |
| Win+Shift+S con una abierta | Como un clic afuera. |

- Pasar a pestaña conserva **todo**: es el mismo `EditorView` (deshacer, selección, idea abierta) que cambia de ventana, no una instancia nueva. *(Decisión del agente: perder Ctrl+Z por querer hacer zoom sería un castigo.)* En la pestaña corre el ajuste de zoom inicial como en cualquier pestaña recién creada.
- Las ventanas que abre el editor en el lugar como dueño (Ajustes, `ConfirmDialog`, «Guardar como», novedades) no cuentan como pérdida de foco: al cerrarlas se vuelve al editor en el lugar. Cualquier otra ventana sí, también las de Popete (pestañas, historial, menú de la bandeja). Alt+` (historial rápido) con una captura en el lugar abierta hace lo mismo que Win+Shift+S: primero copia y cierra esa, y el destino donde pegar es la ventana que queda al frente. *(Decisión del agente.)*
- Copiar pasa por `ClipboardHelper.SetPng`, que registra la huella antes (nota 4): copiar no abre una pestaña nueva.
- La captura queda en el historial en todos los casos (Alt+`, Retomar, etc. la ven como a cualquier otra).
- Al terminar, el foco vuelve al programa que estaba al frente antes de Win+Shift+S (o al que recibió el clic).

### Escalas: PerMonitorV2

- El proceso pasa a PerMonitorV2 (manifiesto de la aplicación), **para todos los usuarios y en los dos modos** (lo eligió el usuario: «todo Popete se ve nítido»). Por eso esta revisión va al principio del plan y se prueba en los dos modos. Todas las ventanas de Popete se ven nítidas en monitores con otra escala, y la foto y el editor en el lugar se calculan por monitor.
- Hay que revisar lo que hoy asume el DPI del sistema: `QuickHistoryWindow.PlaceOnMouseMonitor` (`GetDpiForSystem`), `EditorShell.PlaceFor` (`SystemParameters.WorkArea`), el zoom del editor al pasar la ventana a un monitor con otra escala (`1 / DpiScale`, niveles en píxeles de pantalla), el cursor de mano (`GrabCursor`, por DPI), `BadgeLayout`, el menú de la bandeja (WinForms) y los textos de AGENTS.md que dicen «la app corre con el DPI del sistema».
- El usuario tiene un solo monitor al 100 %; la prueba con escalas distintas se hace cambiando la escala en Configuración o con un segundo monitor.

## Restricciones

- Las del proyecto (AGENTS.md): Windows, .NET 10, WPF + WinForms con nombres completos, sin `MessageBox`, colores por recurso, textos en español, un archivo por tipo y `EditorView` partido por área.
- `EditorView` hoy depende de `EditorShell` (`_shell`: dueño de los diálogos, `Dismiss`, `Activated`/`Deactivated`, `Replace`). El editor en el lugar tiene que reusar `EditorView`, no copiarlo: hace falta separar lo que pide de la ventana que lo aloja (por ejemplo, una interfaz que implementan `EditorShell` y la ventana nueva). Cómo, queda al builder.
- El MSIX tiene que seguir pasando la certificación de la Store: el hook de teclado es de una app de escritorio de confianza completa (PowerToys hace lo mismo). Puede que algún antivirus lo mire con sospecha: es un riesgo aceptado.

## Lo que queda al builder

- Cómo se separa `EditorView` de su ventana, y si el editor en el lugar es una ventana del tamaño de captura + franja + barra o una ventana transparente del monitor entero (en ese caso, lo transparente tiene que dejar pasar los clics).
- La composición exacta de la barra flotante y su estilo (Windows 11, como el resto).
- El ancho de la franja dentro del rango dicho.
- Los nombres de tipos y archivos (por ejemplo `KeyboardHook.cs`, `ScreenSnip.cs`, `InPlaceEditor.cs`).
- Documentar en AGENTS.md (arquitectura, notas nuevas y pruebas manuales nuevas), como en cada cambio.
