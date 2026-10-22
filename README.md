# Anotador de capturas

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

### Herramientas

| Herramienta | Tecla | Notas |
|---|---|---|
| Lápiz | `P` | Trazo libre |
| Resaltador | `H` | Semitransparente; tiene su propio color (amarillo por defecto) |
| Línea | `L` | `Shift` para ángulos de 45° |
| Flecha | `A` | `Shift` para ángulos de 45° |
| Rectángulo | `R` | `Shift` para cuadrado |
| Texto | `T` | Clic para escribir; `Esc` o clic fuera para terminar |
| Borrador | `E` | Borra trazos completos |

Hay 8 colores (rojo, amarillo, verde, azul, morado, naranja, negro y blanco) y 4 grosores, con las teclas `1` a `4`. Sobre la imagen, el puntero muestra el tamaño y el color reales del trazo y se ve tanto en fondos claros como oscuros.

### Tema y capturas recientes

- El botón de la luna elige el tema: **Claro**, **Oscuro** o **Usar el tema de Windows** (por defecto). La elección se guarda en `%LOCALAPPDATA%\AnotadorCapturas\ajustes.json`.
- El botón del reloj (o `Ctrl+H`) muestra una tira con las capturas recientes para abrir otra sin ir al historial. Siempre arranca oculta.

### Atajos

| Atajo | Acción |
|---|---|
| `Ctrl+C` | Copiar la imagen anotada y cerrar el editor |
| `Ctrl+Enter` | Copiar y cerrar (sin esperar el parpadeo) |
| `Ctrl+S` | Guardar como PNG o JPG |
| `Ctrl+Z` | Deshacer |
| `Ctrl+rueda`, `Ctrl+` / `Ctrl-` | Zoom |
| `Ctrl+0` | Ajustar a la ventana |
| `1` a `4` | Grosor: fino, medio, grueso, muy grueso |
| `Ctrl+H` | Mostrar u ocultar las capturas recientes |
| `Esc` | Cerrar el editor |

### Menú de la bandeja

- **Retomar captura:** las 12 más recientes, con miniatura.
- **Ver historial…:** todas las capturas, con opciones para retomar y eliminar.
- **Abrir imagen del portapapeles:** abre lo que haya en el portapapeles. Si ya está en el historial, abre esa sesión.
- **Pausar detección**
- **Iniciar con Windows**
- **Salir**

Doble clic en el ícono abre la captura más reciente.

## Historial

Se guarda en `%LOCALAPPDATA%\AnotadorCapturas\Historial\`, con una carpeta por captura:

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
