# Ideas por número — diseño

Fecha: 2026-10-26. Estado: aprobado por el usuario en conversación; pendiente de plan.

## Para qué

Cuando el usuario numera una captura, cada número señala algo («esto falla», «acá debería decir X»). Hoy lo que quiso decir con cada número se pierde, o tiene que escribirlo aparte al pasarle la captura a una IA. La funcionalidad deja anotar una **idea** por número: un texto que **no sale en la imagen** pero que viaja con ella al copiar. Así la IA recibe la imagen numerada y la lista «1. …, 2. …» para cruzarlas.

El objetivo de entrega son los **chats de IA del navegador** (claude.ai, ChatGPT…) y **Claude Code** (terminal o extensión de VS Code).

## Objetivos

- Anotar la idea de un número sin cortar el flujo rápido de dibujar → copiar y cerrar.
- Que copiar una captura con ideas lleve imagen y texto en una sola pegada.
- Que una captura **sin** ideas se copie exactamente igual que hoy.

## No objetivos

- Juntar las ideas de varias capturas (pestañas o historial) en un solo informe. Hoy el alcance es **una captura**.
- Mostrar las ideas en la imagen exportada.
- Integrarse con una API de IA: Popete solo arma el portapapeles (y un archivo de texto).

## Anti-objetivos (aunque funcione, sería un fracaso)

- Que Word, Outlook, Teams o Paint empiecen a pegar texto en vez de la imagen en capturas **sin** ideas.
- Que las capturas viejas cambien de numeración al reabrirlas.
- Que el campo de idea deje rastro (número nuevo, marca, línea vacía en la lista) cuando se cierra sin escribir.

## Paso 0: prueba desechable del portapapeles (antes de todo)

Antes de construir nada, una prueba rápida y desechable: poner en el portapapeles el PNG que arma `ClipboardHelper.SetPng` (formato `"PNG"` + bitmap) **más** texto Unicode, y pegar en:

- chats de IA del navegador (al menos claude.ai y ChatGPT);
- Claude Code en la terminal y en la extensión de VS Code.

El usuario pega y cuenta qué llega (imagen, texto o las dos).

**Cómo se arma el plan alrededor de la prueba (decisión del usuario):** se construye todo lo que no depende de ella (campo, ideas, numeración, marca, fantasma, lista, **Copiar texto**, `ideas.txt`, encabezado en Ajustes). La **última tarea**, condicionada al resultado, pone el texto junto a la imagen en Copiar y en Alt+`. Si en algún destino no llegan las dos cosas, se frena solo esa tarea y se vuelve a hablar de la entrega con el usuario. Un respaldo ya decidido es el botón **Copiar texto** (ver abajo); otra opción posible para Claude Code es el archivo de texto en la carpeta de la captura. Office y Paint no son criterio de la prueba: la regla «texto solo si hay ideas» ya los protege en las capturas sin ideas.

## Comportamiento

### Qué lleva número

| Elemento | Numeración prendida | Numeración apagada («solo lo que tiene idea») |
|---|---|---|
| Trazo común (lápiz, formas, resaltador) | Siempre | Solo si tiene idea |
| Flecha **libre** (no empieza sobre otro objeto) | Siempre (cambio: hoy ninguna flecha se numera) | Solo si tiene idea |
| Flecha **unida** (empieza sobre otro objeto) | Nunca | Nunca |
| Flecha de una captura vieja (sin el dato de origen) | Nunca (se trata como unida) | Nunca |
| Texto | Siempre | Solo si tiene idea |

- **«Empieza sobre otro objeto»** usa la misma prueba que hoy decide el color de la flecha: `StrokeAt(_start)` (trazo bajo el inicio, 8 px en pantalla, sin resaltadores). Se decide **al dibujar** y se guarda en el trazo. *(Decisión del agente.)*
- Para que las flechas viejas queden como unidas sin migrar nada, se recomienda marcar las **libres** con una propiedad ISF nueva (por ejemplo `FreeArrowProperty`) y no las unidas: una flecha con `ArrowProperty` y sin esa marca sigue sin número, como hoy. *(Decisión del agente; el builder puede elegir otro mecanismo con el mismo resultado.)*
- Si se borra el objeto del que salía una flecha unida, la flecha sigue unida (sin número). *(Decisión del agente.)*
- La idea de una flecha unida se escribe en el número del objeto de donde sale; la flecha nunca abre campo ni toma número.
- **Numeración apagada** pasa a significar «numerar solo lo que tiene idea»: los números con idea van **seguidos** (1, 2, 3), sin huecos. El campo se sigue abriendo después de cada trazo (salvo las flechas unidas). El interruptor de la barra (`N`), su tooltip y el ajuste de Ajustes («Numerar trazos y textos en capturas nuevas») se renombran para reflejarlo, por ejemplo «Numerar todo». *(El nombre exacto queda al builder.)*

### Escribir la idea: el campo junto al número

- Al soltar un trazo que lleva o puede llevar número (todos salvo las flechas unidas) se abre un **campo** junto a su número, con el foco puesto. Con la numeración apagada, el número todavía no se ve: el campo se abre donde iría.
- **Enter** guarda y cierra. **Shift+Enter**, salto de línea. **Esc** cierra guardando lo escrito. *(Decisión del agente.)*
- Clic en el lienzo, empezar otro trazo, cambiar de pestaña, copiar, guardar o cerrar: el campo se cierra guardando lo que tenga.
- **Campo vacío al cerrarse = no pasó nada:** sin idea, sin marca, sin entrada en el deshacer, y con la numeración apagada sin número.
- **Teclas con el campo abierto (decisión del usuario):** el campo tiene el foco y **las letras y los dígitos escriben** (así que después de un trazo no se cambia de herramienta con una letra ni de grosor con 1–3 hasta cerrar el campo). Con el campo **vacío**, los atajos sin letra siguen siendo del editor y cierran el campo sin rastro: **Tab** (alterna herramienta), **Supr** (borra lo recién dibujado), **Ctrl+Z** (deshace el trazo), **Esc**, y los atajos con Ctrl (Ctrl+C, Ctrl+Enter, Ctrl+S, Ctrl+rueda…). Con el campo **con texto**, se porta como un cuadro de texto: Supr y Ctrl+Z editan el texto, y el resto se comporta como hoy con un texto en edición (`_editing`), incluido el clic derecho, que no copia con un cuadro abierto (nota 34). Con el campo vacío, el clic derecho copia como siempre. *(El detalle con texto es decisión del agente: copiar el comportamiento actual de la edición de textos.)*
  - **Ajustes tras la revisión (2026-10-26, aprobados por el usuario):** «vacío» quiere decir «recién abierto y sin tocar»: si se escribe y se borra, ya es un cuadro de texto y Supr/Retroceso/flechas no pasan al editor. Ctrl, Alt y Shift solos no cierran nada; Ctrl+V y AltGr (Ctrl+Alt) escriben; con Ctrl solo pasan los atajos del editor. Esc también suelta la selección (como el Esc del editor tras un trazo). **Ctrl+Enter** guarda la idea y copia y cierra aunque el campo tenga texto. El doble clic y el clic derecho **sobre el campo** son del campo. El número fantasma no aparece por la selección automática de lo recién dibujado ni por el campo que se ofrece solo.
- Hay que cuidar lo mismo que con `_editing`: el guardado automático no debe meter el campo en `anotada.png` (nota 9 de AGENTS.md); el campo vive fuera de `_surface` o se pospone el guardado.
- Los **textos** no abren el campo solos (se acaba de escribir y el texto ya se ve). Se abre con un clic en su número.
- **Reabrir:** un clic en un número abre su campo (además de seleccionar su trazo, como hoy, nota 27). El doble clic sigue devolviendo el número a su lugar.
- **Agregar una idea a algo sin número (decisión del usuario): número fantasma.** Al seleccionar un elemento que no muestra número (numeración apagada y sin idea, o un trazo cuyo campo se cerró vacío) aparece un número tenue donde iría, con el número que tomaría. Un clic en él abre su campo. Es solo del editor: no se exporta, no cuenta para la lista y desaparece al soltar la selección. Las flechas unidas no muestran fantasma: su idea se escribe seleccionando el objeto de donde salen (no se guarda cuál es). *(Esto último y el aspecto del fantasma son decisión del agente o del builder.)*
- Escribir o abrir el campo cuenta como tocar la captura (`MarkTouched`): apaga el cierre automático.

### Ver las ideas en el editor

- Un número con idea lleva una **marca** visible solo en el editor (forma a elección del builder, en el estilo de la app), nunca en lo exportado.
- **Tooltip** al pasar el ratón sobre el número: muestra la idea.
- **Barra de estado:** un indicador «N ideas» (oculto con 0) que abre un desplegable con la lista «1. idea», en el orden de los números. Un clic en una fila lleva a ese número (desplaza la vista si está fuera) y abre su campo. Un botón **Copiar texto** copia solo el texto (encabezado + lista).

### Copiar

- Si la captura tiene al menos una idea, todo lo que copia la captura pone en el portapapeles el PNG **y además** el texto: «Copiar y cerrar», «Copiar», Ctrl+C, Ctrl+Enter, el clic derecho y el historial rápido (Alt+`). Si no tiene ideas, el portapapeles queda **exactamente** como hoy.
- **Texto:** encabezado, línea en blanco y la lista con el número real de cada idea (puede saltear números con la numeración prendida), solo los números con idea, en orden. Si una idea tiene varias líneas, las siguientes van con sangría. Ejemplo:

  ```
  En la imagen, cada número marca un punto:

  1. El total no suma el descuento.
  3. Este botón debería decir «Guardar».
     Y estar a la derecha.
  ```

- **Encabezado configurable** en Ajustes (cuadro de texto). De fábrica: «En la imagen, cada número marca un punto:». Vacío = solo la lista, sin línea en blanco. *(El texto de fábrica es decisión del agente.)*
- Copiar con el campo abierto lo guarda antes de exportar.
- Con la numeración apagada, los números de las ideas **sí** salen en la imagen exportada (sin ellos la lista no se entiende). La marca y el fantasma, no. «Guardar como» (Ctrl+S) guarda solo la imagen, como hoy. *(Decisiones del agente.)*
- La huella se sigue registrando antes de tocar el portapapeles (nota 4). La copia con texto, además, cae en el filtro de la nota 47 (`CopyFormats`), así que la detección tampoco la tomaría por una captura nueva.

### Historial rápido (Alt+`)

Pega imagen y texto con la misma regla, sin abrir el editor. Para eso, cada guardado de la sesión escribe la lista (sin el encabezado, que se agrega al copiar con el valor actual del ajuste) en un archivo de texto de la carpeta de la captura, por ejemplo `ideas.txt`, con escritura atómica como el resto. Sin ideas, el archivo no existe (o se borra). *(Decisión del agente: también deja las ideas legibles para Claude Code u otra herramienta que lea la carpeta.)*

## Persistencia y deshacer

- La idea vive en el elemento: propiedad ISF de texto en el trazo (como `OrderProperty`/`NumberOffsetProperty`) y un campo nuevo en `TextNote` (por ejemplo `Idea`). Así viaja sola con mover, recortar, borrar y deshacer.
- Las coordenadas no cambian nada (nota 16): la idea no tiene posición propia.
- Escribir, cambiar o borrar el texto de una idea es **un paso** de deshacer (un `IUndo` nuevo o `ActionChange`). Borrar un trazo se lleva su idea, y deshacer lo trae de vuelta. *(Decisión del agente.)*
- Las anotaciones cargadas al retomar no entran en el deshacer (nota 10): tampoco sus ideas.
- Recortar: si un trazo queda totalmente afuera y se borra (`ApplyCrop`), su idea se va con él.

## Restricciones del proyecto que aplican

- Convenciones de AGENTS.md: textos y comentarios en español, un archivo por área (`EditorView.Ideas.cs`), colores por recurso (nota 11), nada de `MessageBox`, `PreviewKeyDown` en el editor (nota 3), botones de la barra `Focusable=False` (nota 13).
- El portapapeles se escribe solo con `ClipboardHelper` (ampliar `SetPng` o agregar una variante con texto).
- Sin tests automatizados: agregar una prueba manual numerada al final de «Prueba manual» de AGENTS.md y documentar las decisiones nuevas en «Decisiones y trampas conocidas».

## Queda al builder

- La forma visual de la marca y del campo, y dónde se ubica el campo respecto del número (sin taparlo).
- El nombre del interruptor y del ajuste de numeración.
- El mecanismo exacto para distinguir flechas libres de unidas, siempre que las viejas queden sin número.
- El nombre del archivo de texto y de las propiedades ISF nuevas.
- Cómo reacomoda el layout de la barra de estado el indicador «N ideas».
