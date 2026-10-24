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

## Paquete MSIX (Microsoft Store)

```powershell
powershell -File tools\empaquetar-msix.ps1
```

Genera `artifacts\msix\Popete_1.0.0.0_x64-prueba.msix`, firmado con un certificado de prueba que el script crea en tu usuario. Para instalarlo, confía una vez en ese certificado (como administrador) y luego instala el paquete; el script imprime los dos comandos. Si no tienes el Windows SDK, el script descarga una vez `makeappx`, `makepri` y `signtool` del paquete NuGet oficial `Microsoft.Windows.SDK.BuildTools`.

Para la Store, `-Store` usa la identidad de **Partner Center › Identidad del producto** (ya está en el script; con `-IdentityName`, `-Publisher` y `-PublisherDisplayName` se puede usar otra). Sale sin firmar, porque la Store lo firma:

```powershell
powershell -File tools\empaquetar-msix.ps1 -Store -Version 1.0.0.0
```

La versión tiene que terminar en `.0` y subir en cada envío. Instalada como MSIX, la app guarda sus datos en la carpeta del paquete (se borran al desinstalarla), y **Iniciar con Windows** usa la tarea de inicio del paquete, que también se puede apagar en **Configuración › Aplicaciones › Inicio**.

### Publicar con GitHub Actions

El workflow **Publicar en la Microsoft Store** (`.github/workflows/publicar-store.yml`) publica solo al subir un tag `vX.Y.Z`:

```powershell
git tag v1.0.2
git push origin v1.0.2
```

Eso genera el `.msix` `1.0.2.0` con el mismo script, crea el Release `v1.0.2` con el paquete adjunto y lo envía a certificación. La versión tiene que ser mayor que la última enviada a la Store. Los push a `main` no publican nada.

También se puede lanzar a mano desde **Actions › Run workflow**, con la versión (`N.N.N.0`) y cada paso opcional:

- siempre genera el `.msix` y lo deja como artefacto de la ejecución;
- si **crear_release** está marcado (lo está por defecto), crea el Release `vN.N.N` con el `.msix` adjunto;
- si **enviar_store** está marcado, lo sube a Partner Center. Sin **confirmar_envio** queda como borrador, para revisarlo y enviarlo desde Partner Center; con **confirmar_envio** va directo a certificación.

Si hay un borrador pendiente en Partner Center, el envío lo reemplaza. Enviar a la Store requiere que la app ya esté **publicada y en vivo** (la primera versión se envía a mano) y una app de Microsoft Entra asociada a Partner Center con rol *Manager*. En el repo, **Settings › Secrets and variables › Actions**:

| Nombre | Tipo | Valor |
|---|---|---|
| `PARTNER_CENTER_TENANT_ID` | Secreto | Id. de inquilino de la app de Entra |
| `PARTNER_CENTER_SELLER_ID` | Secreto | Id. de vendedor (Partner Center › Configuración de la cuenta) |
| `PARTNER_CENTER_CLIENT_ID` | Secreto | Id. de cliente de la app de Entra |
| `PARTNER_CENTER_CLIENT_SECRET` | Secreto | Clave de la app de Entra |
| `MSSTORE_PRODUCT_ID` | Variable | El **Store ID** del producto (Partner Center › Identidad del producto) |

## Uso

1. Ejecuta la app. Aparece un ícono en la bandeja del sistema.
2. Toma una captura con **Impr Pant**, **Win+Shift+S**, la Herramienta Recortes o **Win+Impr Pant**.
3. Se abre el editor con la captura. Anota lo que necesites.
4. Pulsa **Ctrl+C** para copiar la imagen anotada: la imagen parpadea para confirmarlo y el editor se cierra solo. **Ctrl+S** la guarda como PNG o JPG. También copia y cierra un **clic derecho** sobre la imagen.

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
| Flecha | `A` | Sigue el mouse (curva); si sale casi recta se endereza sola. `Shift`: recta a 45°. Si empieza sobre un trazo, toma su color |
| Rectángulo | `R` | `Shift` para cuadrado |
| Óvalo | `O` | `Shift` para círculo |
| Texto | `T` | Clic para escribir; `Esc` o clic fuera para terminar |
| Borrador | `E` | Borra trazos completos |
| Recortar | `C` | Arrastra los bordes de la captura; ver «Recortar» más abajo |

Resaltador, Línea, Rectángulo, Óvalo, Texto y «Texto con fondo» están en el botón **Otras herramientas** (el de las figuras, con una flechita), al lado de la flecha. El botón muestra la que esté activa. Sus teclas (`H`, `L`, `R`, `O`, `T`) siguen funcionando.

**Organizar la barra:** arrastra cualquier botón de herramienta (también el de Otras herramientas) para cambiarlo de lugar. Clic derecho en un botón: **Pasar a Otras herramientas**; clic derecho en una herramienta del menú Otras: la pone en la barra. **Restablecer la barra** (clic derecho en cualquier botón) vuelve al orden de fábrica. Se recuerda entre sesiones.

El editor abre con la última herramienta que usaste, también después de reiniciar la app (salvo Recortar: en ese caso, el lápiz).

Los trazos llevan un borde blanco fino para que se destaquen sobre cualquier fondo (también en la imagen copiada); el resaltador no.

Hay 8 colores (rojo, amarillo, verde, azul, morado, naranja, negro y blanco) y 3 grosores (2, 4 y 8 px), con las teclas `1` a `3`. Sobre la imagen, el puntero muestra el tamaño y el color reales del trazo, cuatro marcas de retícula y el grosor ("4 px"), y se ve tanto en fondos claros como oscuros.

El lápiz (con las formas y el texto) y el resaltador recuerdan cada uno su color: rojo y amarillo al empezar.

### Lo recién dibujado queda seleccionado

Después de un trazo o una forma, queda marcado unos segundos con un recuadro punteado (3 por defecto; se cambia en Ajustes): elegir un color o un grosor se le aplica, y `Supr` lo borra. Cada retoque reinicia la cuenta. `Esc` quita la selección. Con **Seleccionar** (`V`) se puede elegir cualquier trazo anterior, y queda seleccionado hasta que lo sueltes.

### Numeración automática

Cada trazo y cada texto (las flechas no) lleva un círculo con su número, en el color del trazo, pegado al punto del trazo que está más arriba a la izquierda (en un círculo, sobre su arco superior izquierdo), sin taparlo. Si borras uno, los demás se renumeran. Sale en la imagen copiada. El botón **123** (o `N`) la prende o apaga en cada captura; el valor inicial y el tamaño de los números (normal, grande, muy grande, enorme) se eligen en Ajustes.

**Arrastrar un número** lo cambia de lugar (su trazo o texto no se mueve); si después mueves el trazo, el número lo acompaña. **Doble clic** en el número lo devuelve a su lugar automático. Se deshace con Ctrl+Z.

### Un color distinto por trazo

Después de cada trazo, el color pasa solo al siguiente: rojo → azul → verde → morado → naranja (el resaltador: amarillo → verde → azul). Así cada paso numerado queda de un color distinto. Si eliges un color a mano, el próximo trazo lo usa y la rotación sigue desde ahí. Se apaga en Ajustes.

### Mover lo dibujado

Con **Seleccionar** (`V`), un clic sobre un trazo o un texto lo elige y arrastrarlo lo mueve. Para elegir **varios**, arrastra un recuadro desde una zona vacía: queda seleccionado todo lo que toque. `Shift` + recuadro suma a lo que ya había, `Shift`+clic agrega o quita uno, y `Ctrl+A` elige todo. Arrastrar cualquiera de los elegidos mueve todos juntos; el color, el grosor y `Supr` se aplican a todos.

**Clic en el número** de un trazo o texto lo selecciona con cualquier herramienta (arrastrar el número mueve solo el número; ver «Numeración automática»). Con cualquier herramienta, **doble clic sobre un trazo** lo elige (sin cambiar de herramienta). **Clic derecho** sobre la imagen copia y cierra.

Con algo seleccionado, las flechas del teclado lo corren 1 px (con `Shift`, 10 px). Todo se deshace con Ctrl+Z.

### Reajustar un trazo

Elige **un** trazo (doble clic sobre él, un clic con **Seleccionar** o un clic en su número) y aparecen asas blancas:

- **Línea:** un asa redonda en cada extremo; arrástrala para mover ese extremo (`Shift`: 45°).
- **Flecha** (recta o a mano alzada): asas en el **inicio**, la **punta** y el **medio**. Los extremos se mueven y la curva conserva su forma (gira y se estira); el asa del medio la dobla, y una flecha recta pasa a ser curva. La cabeza se rehace en la punta.
- **Rectángulo y óvalo:** asas en las esquinas y los lados para cambiar el tamaño.
- **Lápiz y resaltador:** las mismas asas estiran o encogen el trazo (el grosor no cambia).

Las asas no salen en lo recién dibujado (para que puedas empezar otro trazo justo al lado). Los rectángulos, óvalos y líneas de capturas anteriores a la versión 1.0.2 solo se pueden estirar con el recuadro. Cada arrastre se deshace con Ctrl+Z.

### Texto con fondo

**Texto con fondo** (en **Otras herramientas**) hace que los textos salgan como **etiqueta**: fondo redondeado del color elegido y letra blanca (oscura si el color es claro). Se aplica a los textos nuevos y, si hay un texto seleccionado, también a ese. Se recuerda entre sesiones.

### Abrir una imagen arrastrándola

Arrastra una imagen (desde el Explorador, el navegador u otra app) sobre un editor o sobre la ventana de historial. Si el editor todavía no tiene anotaciones, la imagen lo reemplaza en la misma ventana; si ya tiene algo, se abre en otra. Sobre el historial, siempre se abre un editor nuevo.

### Corregir un texto

Doble clic sobre un texto lo vuelve a abrir para editarlo, con el cursor al final. Si lo dejas vacío, se borra. Se deshace con Ctrl+Z.

### Recortar

Con **Recortar** (`C`) aparece la captura completa con un marco: arrastra sus **bordes** o **esquinas** hacia adentro (o de nuevo hacia afuera), o arrástralo desde adentro para moverlo. Lo que queda afuera se oscurece. **Enter**, volver a tocar **Recortar** (botón o `C`), cambiar de herramienta, copiar o guardar aplican el recorte (las dos primeras vuelven a la herramienta que tenías); **Esc** lo cancela. Lo dibujado que quede totalmente afuera se borra; lo que cruza el borde se mantiene. La captura original no se toca: **Quitar recorte** (en la barra de estado) vuelve a la imagen completa, y Ctrl+Z / Ctrl+Y deshacen y rehacen el recorte.

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
| `Ctrl+C` o clic derecho en la imagen | Copiar la imagen anotada y cerrar el editor |
| `Ctrl+Enter` | Copiar y cerrar (sin esperar el parpadeo) |
| `Ctrl+S` | Guardar como PNG o JPG |
| `Ctrl+Z` | Deshacer |
| `Ctrl+Y` o `Ctrl+Shift+Z` | Rehacer |
| `N` | Prender o apagar la numeración |
| `Ctrl+,` | Ajustes |
| `Ctrl+rueda`, `Ctrl+` / `Ctrl-` | Zoom |
| `Shift+rueda` | Desplazar en horizontal |
| Clic central y arrastrar | Mover la vista (con la mano), aunque la imagen entre entera; `Ctrl+0` la vuelve a centrar |
| `Supr` | Borrar lo seleccionado |
| `Ctrl+A` | Seleccionar todo |
| `Enter` / `Esc` con Recortar | Aplicar / cancelar el recorte |
| `Ctrl+0` | Ajustar a la ventana |
| `1` a `3` | Grosor: fino, medio, grueso |
| `Tab` | Alternar entre lápiz y flecha |
| `Ctrl+H` | Mostrar u ocultar las capturas recientes |
| `Esc` | Quitar la selección; si no hay, cerrar el editor |

### Menú de la bandeja

- **Actualizar Popete:** solo aparece cuando hay una versión nueva en la Store (ver abajo).
- **Retomar captura:** las 12 más recientes, con miniatura.
- **Ver historial…:** todas las capturas, con opciones para retomar y eliminar.
- **Abrir imagen del portapapeles:** abre lo que haya en el portapapeles. Si ya está en el historial, abre esa sesión.
- **Pausar detección**
- **Iniciar con Windows**
- **Salir**

Un clic en el ícono abre la captura más reciente.

**Versión nueva:** instalada desde la Store, Popete pregunta si hay una versión nueva al rato de arrancar y cada 6 horas (la Store no actualiza una app abierta, y Popete casi siempre lo está). Si la hay, avisa con un globo en la bandeja; un clic en el globo o en **Actualizar Popete** cierra los editores (se guardan), instala la versión nueva y Popete vuelve a abrirse. Si la Store no permite instalarla así, se abre la página de Popete en la Store. El `.exe` suelto no avisa.

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
