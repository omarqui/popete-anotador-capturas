---
name: publicar-store
description: Publicar una versión nueva de Popete en la Microsoft Store desde Partner Center con Claude in Chrome (la cuenta es Individual y no tiene API). Úsala cuando el usuario pida subir, publicar o enviar una versión a la Store o a certificación.
---

# Publicar en la Microsoft Store

La cuenta de Partner Center es **Individual**: no admite apps de Microsoft Entra, así que no hay API ni `msstore`, y el workflow `publicar-store.yml` saltea la Store. Se publica desde el navegador, con la sesión que el usuario ya tiene iniciada en su Chrome. Nunca escribas su contraseña: si Partner Center pide iniciar sesión, pide al usuario que la inicie.

Producto: **Popete · anotador de capturas**, Store ID `9N90ZWQLXHZ0`. Overview: https://partner.microsoft.com/en-us/dashboard/products/9N90ZWQLXHZ0/overview. Partner Center está en inglés.

## 1. Qué está publicado

Abre el overview en una pestaña nueva (`tabs_context_mcp` primero).

- **Update in draft**: ya hay un borrador, quizá de un intento anterior. Revísalo antes de crear otro.
- **In certification**: hay un envío en curso y no se puede empezar otro sin cancelarlo. Pregunta.

**View product details** → Packages muestra la versión en vivo. Compárala con el último tag `vX.Y.Z` (`git tag --list "v*"`). Si no coinciden, aclara con el usuario cuál fue la última enviada antes de seguir.

## 2. Novedades (antes del paquete)

Van en `novedades.txt`, en la raíz del repo: Popete las muestra después de actualizar (las pide a la Store y, si no responde, usa esa copia, embebida en el `.exe`), así que se escriben y se **commitean antes** de armar el paquete. Redáctalas a partir de los commits desde el último tag (`git log vX.Y.Z..HEAD`, o el README si hace falta más detalle): para usuarios, en español con tuteo, viñetas «•», sin nombres de clases ni detalles internos.

- La versión nueva, completa: «Novedades de la versión X.Y.Z:» y sus viñetas. Esa primera línea es obligatoria: el script falla si no dice la versión que arma, y Popete la usa para saber si las de la Store son de la versión instalada.
- Las anteriores, **compactadas en una línea cada una** («Versión 1.1: …»), como pidió el usuario (2026-10-25).
- Máximo **1500 caracteres** (el script lo comprueba). Si no entra, compacta más o quita las más viejas.

Muéstrale el texto al usuario antes de commitearlo. Guardarlo en el borrador no publica nada; lo vuelve a ver en la confirmación del paso 6.

## 3. Paquete

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\preparar-store.ps1
```

Calcula la versión (sube el tercer número del último tag; con `-Version 1.2.0.0`, otra), exige el árbol limpio y que `novedades.txt` sea de esa versión. Genera `artifacts\msix\Popete_<versión>_x64.msix` sin firmar y guarda los commits desde el último tag en `Popete_<versión>_commits.txt`, en la misma carpeta. Al final imprime las novedades y las deja en el portapapeles. Pásale `-NoAbrir`: el Explorador con el paquete marcado solo hacía falta cuando lo arrastraba el usuario. Tarda un par de minutos (`dotnet publish` self-contained). Imprime el commit del paquete: anótalo para el tag (paso 7).

Si falla con «no es mayor que la ultima enviada», esa versión ya tiene tag: ya se mandó.

## 4. Servidor local

La extensión no puede subir el paquete: `file_upload` admite hasta 10 MB y el paquete pesa unos 78 MB, y un clic en «browse your files» no abre el cuadro «Abrir» de Windows. Lo que sí funciona (2026-10-27): la **página** de Partner Center lo descarga de un servidor local con `fetch` y lo pone en su `<input type=file>`. Lo mismo con las novedades. En segundo plano (`run_in_background`):

```powershell
node tools/servir-paquete.mjs artifacts/msix/Popete_<versión>_x64.msix
```

Sirve `/paquete.msix`, `/novedades.txt` y `/info` (`{ nombre, bytes, version }`) en `http://127.0.0.1:8765`, con CORS solo para `partner.microsoft.com`. Se cierra solo cuando sirvió el paquete y las novedades, o a los 30 minutos. Si hay que repetir algo, vuelve a lanzarlo.

**Permiso de red local de Chrome:** la primera vez que la página pide algo a `127.0.0.1`, Chrome pregunta si `partner.microsoft.com` puede acceder a la red local, y mientras el aviso está abierto la pestaña no responde (`javascript_tool` y las capturas se cortan por tiempo). El usuario lo permitió el 2026-10-27 y queda guardado para el sitio. Si vuelve a pasar, pregúntale con `AskUserQuestion` si ve el aviso y que lo permita él: no lo aceptes tú.

## 5. Borrador en Partner Center

1. Overview → **Start update**: crea «Submission N» como copia de la anterior. Espera unos segundos a que carguen los estados.
2. **Store listings** (la flecha de la fila) → **Spanish (Spain)**. Con `javascript_tool`:

   ```js
   const text = await (await fetch('http://127.0.0.1:8765/novedades.txt')).text();
   const ta = document.getElementById('releaseNotes'); // «What's new in this version»
   Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(ta, text);
   ta.dispatchEvent(new Event('input', { bubbles: true }));
   ta.dispatchEvent(new Event('change', { bubbles: true }));
   `${ta.value.length} caracteres: ${ta.value.slice(0, 40)}`
   ```

   Comprueba que empiece con «Novedades de la versión X.Y.Z:» → **Save** (al pie; vuelve al overview).
3. **Packages** (la fila del overview). Con `javascript_tool`:

   ```js
   const info = await (await fetch('http://127.0.0.1:8765/info')).json();
   const blob = await (await fetch('http://127.0.0.1:8765/paquete.msix')).blob();
   const input = document.querySelector('input.fileuploader[type=file]');
   const dt = new DataTransfer();
   dt.items.add(new File([blob], info.nombre, { type: 'application/octet-stream', lastModified: Date.now() }));
   input.files = dt.files;
   input.dispatchEvent(new Event('change', { bubbles: true }));
   `${info.nombre}: ${blob.size} de ${info.bytes} bytes`
   ```

   El cargador toma el archivo y vacía el `input` (leer `input.files` después da 0). Aparece «Uploading», luego «Analyzing package» y «Validating…»: tarda unos 30 s.

Después, `get_page_text`:

- Tiene que figurar el paquete nuevo con su versión.
- El aviso «restricted capabilities… runFullTrust» es normal: lo tienen todas las versiones.
- El paquete anterior figura con «This package will be removed after you save this page»: **Save** (al pie).

En el overview, Packages debe decir **Validated** y Store listings **Updated**.

**Cuidado:** lo que se sube queda en el borrador al instante, sin «Save», y recargar la página no lo quita. En la prueba del 2026-10-27 se subió dos veces el mismo 1.2.0.0. El error «Multiple uploaded files contain the same package» deshabilita **Save** y deja la barra trabada en «Analyzing package 99». Para destrabarlo, en la copia sobrante: «Don't remove this package» → **Remove** → **Save**. Nunca subas una versión que ya está en el borrador.

## 6. Mandar a certificación (con confirmación)

**Submit for certification** publica la versión en cuanto se aprueba, y una vez enviada, para cambiar algo hay que cancelarla. Antes de tocarlo, pregunta con `AskUserQuestion` (tu recomendación primero), con la versión, el commit y el texto de las novedades.

Con el sí, **Submit for certification**. Aparece una encuesta («Your feedback matters to us»): **Cancel**, sin contestarla. El estado pasa a **In certification** (Pre-processing). Tarda de unas horas a 3 días hábiles, y después se publica sola. Cierra la pestaña.

## 7. Tag

El tag es lo que usa `preparar-store.ps1` para saber cuál fue la última enviada: sin él, la próxima vez propondría la misma versión. Pregunta antes de subirlo, porque es un push:

```powershell
git tag vX.Y.Z <commit>     # el que imprimió preparar-store.ps1
git push origin vX.Y.Z
```

El push dispara `publicar-store.yml`. Ese workflow vuelve a generar el `.msix` de ese commit y crea el Release `vX.Y.Z` con el paquete adjunto. Saltea la Store con un aviso porque no hay credenciales: es lo esperado.

Si la certificación falla, Partner Center lo muestra en el overview con el informe. Arregla el problema y prepara la versión siguiente: la fallida ya tiene tag.
