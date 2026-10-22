# Popete · anotador de capturas

App de Windows que vive en la bandeja del sistema. Cuando tomas una captura de pantalla, se abre sola con herramientas para dibujar, subrayar y escribir sobre la imagen. Cada captura queda guardada en un historial y la puedes retomar después.

Es un proyecto WPF de .NET 10. El código está en `src/`.

## Requisitos

- Windows 10 u 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Ejecutar

Desde la raíz del repositorio:

```powershell
dotnet run --project src
```

## Publicar un .exe

```powershell
dotnet publish src
```

Usa el `.exe` publicado si vas a activar **Iniciar con Windows**. Con `dotnet run`, el ejecutable vive en `src\bin\` y cambia con cada compilación.

## Uso

1. Ejecuta la app. Aparece un ícono en la bandeja del sistema.
2. Toma una captura con **Impr Pant**, **Win+Shift+S**, la Herramienta Recortes o **Win+Impr Pant**.
3. Se abre el editor con la captura. Anota lo que necesites.
4. Pulsa **Ctrl+C** para copiar la imagen anotada: la imagen parpadea para confirmarlo y el editor se cierra solo. **Ctrl+S** la guarda como PNG o JPG. Para copiar sin cerrar, usa el botón **Copiar**.

Todo se guarda automáticamente en el historial mientras editas.

Si una captura nueva se abre y te vas a otra ventana sin anotar nada (por ejemplo, porque solo querías pegarla en otro lado), el editor se cierra solo a los 4 segundos. Si vuelves antes, se queda. La captura sigue en el historial. Las capturas que retomas no se cierran solas.

El botón de la papelera quita todas las anotaciones, después de pedir confirmación; se deshace con Ctrl+Z.

### Herramientas

| Herramienta | Tecla | Notas |
|---|---|---|
| Seleccionar | `V` | Clic en un trazo o texto, o arrastra un recuadro para elegir varios; `Supr` los borra |
| Lápiz | `P` | Trazo libre (un clic suelto o un trazo de menos de 5 px no se aplica) |
| Resaltador | `H` | Semitransparente; tiene su propio color (amarillo por defecto) |
| Línea | `L` | `Shift` para ángulos de 45° |
| Flecha | `A` | Sigue el mouse (curva); si sale casi recta se endereza sola. `Shift`: recta a 45° |
| Rectángulo | `R` | `Shift` para cuadrado |
| Texto | `T` | Clic para escribir; `Esc` o clic fuera para terminar |
| Borrador | `E` | Borra trazos completos |
| Recortar | `C` | Arrastra los bordes de la captura; ver «Recortar» más abajo |

Línea, Rectángulo, Texto y «Texto con fondo» están en el botón **Otras herramientas** (el de las figuras, con una flechita), al lado de la flecha. El botón muestra la que esté activa. Sus teclas (`L`, `R`, `T`) siguen funcionando.

Los trazos llevan un borde blanco fino para que se destaquen sobre cualquier fondo (también en la imagen copiada); el resaltador no.

Hay 8 colores (rojo, amarillo, verde, azul, morado, naranja, negro y blanco) y 4 grosores, con las teclas `1` a `4`. Sobre la imagen, el puntero muestra el tamaño y el color reales del trazo, cuatro marcas de retícula y el grosor ("4 px"), y se ve tanto en fondos claros como oscuros.

### Color principal y secundario

Clic en un color elige el **principal**; clic derecho, el **secundario** (los dos se ven superpuestos al inicio de la paleta). Para dibujar rápido con el otro, **toca Alt** o haz **clic derecho sobre la imagen**: se intercambian. El lápiz y el resaltador tienen cada uno su par (rojo/azul y amarillo/verde por defecto).

### Lo recién dibujado queda seleccionado

Después de un trazo o una forma, queda marcado unos segundos con un recuadro punteado (3 por defecto; se cambia en Ajustes): elegir un color o un grosor se le aplica, y `Supr` lo borra. Cada retoque reinicia la cuenta. Tocar Alt o el clic derecho **no** lo cambian (son para el próximo trazo). `Esc` quita la selección. Con **Seleccionar** (`V`) se puede elegir cualquier trazo anterior, y queda seleccionado hasta que lo sueltes.

### Numeración automática

Cada trazo y cada texto lleva un círculo con su número, en el color del trazo, pegado al punto del trazo que está más arriba a la izquierda (en un círculo, sobre su arco superior izquierdo), sin taparlo. Si borras uno, los demás se renumeran. Sale en la imagen copiada. El botón **123** (o `N`) la prende o apaga en cada captura; el valor inicial y el tamaño de los números (normal, grande, muy grande, enorme) se eligen en Ajustes.

### Un color distinto por trazo

Después de cada trazo, el color pasa solo al siguiente: rojo → azul → verde → morado → naranja (el resaltador: amarillo → verde → azul). Así cada paso numerado queda de un color distinto. Si eliges un color a mano, el próximo trazo lo usa y la rotación sigue desde ahí. Se apaga en Ajustes.

### Mover lo dibujado

Con **Seleccionar** (`V`), un clic sobre un trazo o un texto lo elige y arrastrarlo lo mueve. Para elegir **varios**, arrastra un recuadro desde una zona vacía: queda seleccionado todo lo que toque. `Shift` + recuadro suma a lo que ya había, `Shift`+clic agrega o quita uno, y `Ctrl+A` elige todo. Arrastrar cualquiera de los elegidos mueve todos juntos; el color, el grosor y `Supr` se aplican a todos.

**Clic en el número** de un trazo o texto lo selecciona con cualquier herramienta, y arrastrar desde el número lo mueve.

Con algo seleccionado, las flechas del teclado lo corren 1 px (con `Shift`, 10 px). Todo se deshace con Ctrl+Z.

### Texto con fondo

**Texto con fondo** (en **Otras herramientas**) hace que los textos salgan como **etiqueta**: fondo redondeado del color elegido y letra blanca (oscura si el color es claro). Se aplica a los textos nuevos y, si hay un texto seleccionado, también a ese. Se recuerda entre sesiones.

### Abrir una imagen arrastrándola

Arrastra una imagen (desde el Explorador, el navegador u otra app) sobre un editor o sobre la ventana de historial. Si el editor todavía no tiene anotaciones, la imagen lo reemplaza en la misma ventana; si ya tiene algo, se abre en otra. Sobre el historial, siempre se abre un editor nuevo.

### Corregir un texto

Doble clic sobre un texto lo vuelve a abrir para editarlo, con el cursor al final. Si lo dejas vacío, se borra. Se deshace con Ctrl+Z.

### Recortar

Con **Recortar** (`C`) aparece la captura completa con un marco: arrastra sus **bordes** o **esquinas** hacia adentro (o de nuevo hacia afuera), o arrástralo desde adentro para moverlo. Lo que queda afuera se oscurece. **Enter** (o cambiar de herramienta, copiar o guardar) aplica el recorte; **Esc** lo cancela. Lo dibujado que quede totalmente afuera se borra; lo que cruza el borde se mantiene. La captura original no se toca: **Quitar recorte** (en la barra de estado) vuelve a la imagen completa, y Ctrl+Z / Ctrl+Y deshacen y rehacen el recorte.

### Ajustes

Botón de ajustes en la barra de estado, `Ctrl+,` o **Ajustes…** en la bandeja. Tema, numeración por defecto, segundos de selección, grosor inicial, carpeta de «Guardar», cuántas capturas conservar e iniciar con Windows. Los cambios se guardan al tocarlos.

### Dibujar fuera de la captura

Alrededor de la captura hay un margen (marcado con una línea punteada) donde también se puede dibujar o escribir. Al copiar o guardar, la imagen crece lo justo para incluir lo que quede afuera, con fondo blanco. En el editor la captura se ve con esquinas redondeadas; la imagen que se copia queda rectangular.

### Tema y capturas recientes

- El botón de la luna/sol cambia entre tema **claro** y **oscuro**. Hasta que lo toques por primera vez, sigue al tema de Windows. La elección se guarda en `%LOCALAPPDATA%\Popete\ajustes.json`.
- El botón del reloj (o `Ctrl+H`) muestra una tira con las capturas recientes para abrir otra sin ir al historial. Siempre arranca oculta. Cada miniatura tiene una **×** para eliminarla, y arriba están **Eliminar todas** y **Conservar las últimas N** (20, 50, 100, 200 o todas; 50 por defecto): las más viejas se eliminan solas. Las capturas abiertas nunca se eliminan.

### Atajos

| Atajo | Acción |
|---|---|
| `Ctrl+C` o clic central | Copiar la imagen anotada y cerrar el editor |
| `Ctrl+Enter` | Copiar y cerrar (sin esperar el parpadeo) |
| `Ctrl+S` | Guardar como PNG o JPG |
| `Ctrl+Z` | Deshacer |
| `Ctrl+Y` o `Ctrl+Shift+Z` | Rehacer |
| `N` | Prender o apagar la numeración |
| `Ctrl+,` | Ajustes |
| `Ctrl+rueda`, `Ctrl+` / `Ctrl-` | Zoom |
| `Shift+rueda` | Desplazar en horizontal |
| `Alt` (tocar) o clic derecho en la imagen | Intercambiar color principal y secundario |
| `Supr` | Borrar lo seleccionado |
| `Ctrl+A` | Seleccionar todo |
| `Enter` / `Esc` con Recortar | Aplicar / cancelar el recorte |
| `Ctrl+0` | Ajustar a la ventana |
| `1` a `4` | Grosor: fino, medio, grueso, muy grueso |
| `Tab` | Alternar entre lápiz y flecha |
| `Ctrl+H` | Mostrar u ocultar las capturas recientes |
| `Esc` | Quitar la selección; si no hay, cerrar el editor |

### Menú de la bandeja

- **Retomar captura:** las 12 más recientes, con miniatura.
- **Ver historial…:** todas las capturas, con opciones para retomar y eliminar.
- **Abrir imagen del portapapeles:** abre lo que haya en el portapapeles. Si ya está en el historial, abre esa sesión.
- **Pausar detección**
- **Iniciar con Windows**
- **Salir**

Doble clic en el ícono abre la captura más reciente.

## Historial

Se guarda en `%LOCALAPPDATA%\Popete\Historial\`, con una carpeta por captura:

| Archivo | Contenido |
|---|---|
| `original.png` | Captura sin editar |
| `trazos.isf` | Trazos (lápiz, resaltador y formas) en formato Ink Serialized Format |
| `textos.json` | Textos con posición, tamaño y color |
| `anotada.png` | Imagen con las anotaciones |
| `miniatura.png` | Vista previa de hasta 320 px |
| `meta.json` | Fechas, tamaño y huellas para detectar duplicados |

No hay borrado automático. Puedes eliminar capturas desde **Ver historial…** o borrar las carpetas a mano.

## Cómo detecta las capturas

La app no intercepta teclas. Escucha los cambios del portapapeles con `AddClipboardFormatListener`, y todas las formas de capturar de Windows copian la imagen ahí.

Cada imagen se identifica con una huella: tamaño más SHA-256 de los píxeles. Solo se abre el editor si la imagen **nunca se ha visto antes**. Así se ignoran las imágenes que vuelven a publicar programas como Escritorio remoto o el historial del portapapeles, y también las imágenes anotadas que copia la propia app.

Esto tiene dos consecuencias:

- Copiar una imagen desde otro programa (por ejemplo, el navegador) también abre el editor. Usa **Pausar detección** si te estorba.
- Una captura idéntica a una anterior, píxel por píxel, no se vuelve a abrir. Búscala en **Retomar captura**.

## Solución de problemas

| Problema | Causa y solución |
|---|---|
| Error de trimming o AOT con WPF | WPF no soporta AOT. El `.csproj` lo desactiva con `PublishAot=false`; no lo quites. |
| La app no arranca y no da error | Ya hay una instancia corriendo (solo se permite una). Ciérrala desde la bandeja: **Salir**. |
| Ctrl+C no copia mientras escribes un texto | Es intencional: dentro del cuadro de texto, Ctrl+C copia texto. Pulsa `Esc` primero. |

## Privacidad

Popete no envía datos a internet: las capturas, anotaciones y ajustes quedan en tu equipo. Detalles en la [política de privacidad](PRIVACIDAD.md).
