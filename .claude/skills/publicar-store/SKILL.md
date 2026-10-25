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

## 2. Paquete

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\preparar-store.ps1
```

Calcula la versión (sube el tercer número del último tag; con `-Version 1.2.0.0`, otra) y exige el árbol limpio. Genera `artifacts\msix\Popete_<versión>_x64.msix` sin firmar y guarda los commits desde el último tag en `Popete_<versión>_commits.txt`, en la misma carpeta. Al final abre el Explorador con el paquete marcado (`-NoAbrir` para que no). Tarda un par de minutos (`dotnet publish` self-contained). Imprime el commit del paquete: anótalo para el tag (paso 7).

## 3. Novedades

Redáctalas a partir de los commits (con `git log` o el README si hace falta más detalle): para usuarios, en español con tuteo, viñetas «•», sin nombres de clases ni detalles internos.

- La versión nueva, completa: «Novedades de la versión X.Y.Z:» y sus viñetas.
- Las anteriores, **compactadas en una línea cada una** («Versión 1.1: …»), como pidió el usuario (2026-10-25). Salen del texto que ya trae el borrador.
- Máximo **1500 caracteres**. El número a la derecha del cuadro son los que quedan. Si no entra, compacta más o quita las más viejas.

El usuario ve el texto en la confirmación del paso 6: guardarlo en el borrador no publica nada.

## 4. Borrador en Partner Center

1. Overview → **Start update**: crea «Submission N» como copia de la anterior. Espera unos segundos a que carguen los estados.
2. **Store listings** → **Spanish (Spain)** → cuadro **What's new in this version**: `form_input` con el texto completo → **Save** (al pie; vuelve al overview).

## 5. Subir el paquete (lo arrastra el usuario)

La extensión de Chrome **no puede** subirlo. `file_upload` acepta como mucho 10 MB, y solo archivos compartidos con la sesión; el paquete pesa unos 78 MB. Un clic en «browse your files» no abre el cuadro «Abrir» de Windows: la extensión intercepta el selector, y un script de UIAutomation que esperaba la ventana `#32770` no encontró nada. No lo vuelvas a intentar.

Deja la pestaña en **Packages** del borrador (menú izquierdo). Comprueba que el Explorador quedó abierto con el paquete marcado; si no, ábrelo con `explorer.exe /select,"<ruta>"`. Pídele al usuario que lo arrastre al recuadro «Drag your packages here» de la pestaña del grupo de Claude y que te avise.

Cuando avise, `get_page_text`:

- Tiene que figurar el paquete nuevo con su versión.
- El aviso «restricted capabilities… runFullTrust» es normal: lo tienen todas las versiones.
- El paquete anterior figura con «This package will be removed after you save this page»: **Save** (al pie).

En el overview, Packages debe decir **Validated** y Store listings **Updated**.

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
