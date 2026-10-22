# Política de privacidad de Popete

**Última actualización:** 22 de octubre de 2026

Popete (anotador de capturas) es una aplicación para Windows creada por Omarqui Aybar. Esta política explica qué datos usa la aplicación y qué hace con ellos.

## En resumen

- Popete **no envía ningún dato a internet**. No se conecta a servidores, no tiene cuentas de usuario, publicidad ni analíticas.
- Todo lo que guarda queda **en tu equipo**, en tu carpeta de usuario.
- Tú decides qué se conserva: puedes borrar cualquier captura, o todas, cuando quieras.

## Qué datos usa

**Imágenes del portapapeles.** Para detectar las capturas de pantalla, Popete revisa el portapapeles de Windows cada vez que cambia. Solo lee **imágenes**; ignora el texto y los demás formatos. Cuando llega una imagen que no había visto antes, abre el editor con ella. Si no quieres que lo haga por un rato, usa **Pausar detección** en el menú de la bandeja.

**Imágenes que arrastras a la ventana.** Si sueltas una imagen sobre el editor o el historial, Popete la abre igual que una captura.

**Tus anotaciones.** Trazos, formas y textos que dibujas sobre cada captura.

## Qué guarda y dónde

Por cada captura, Popete guarda en tu equipo:

- la imagen original y la versión anotada (PNG), más una miniatura;
- las anotaciones (trazos y textos);
- datos técnicos: fecha y hora, tamaño de la imagen, el recorte aplicado y una "huella" (un resumen SHA-256 de los píxeles) que sirve para no abrir dos veces la misma imagen.

También guarda tus **ajustes** (tema, grosor inicial, etc.) en un archivo `ajustes.json`.

La carpeta es `%LOCALAPPDATA%\Popete`. Si instalaste Popete desde la Microsoft Store, es la carpeta privada de la aplicación dentro de `%LOCALAPPDATA%\Packages`. En ambos casos, el botón **Abrir carpeta del historial** de Ajustes te lleva a ella.

Cuando copias (**Copiar y cerrar**, Ctrl+C) o guardas (**Guardar como**, Ctrl+S), la imagen anotada va al portapapeles o al archivo que tú elijas. A partir de ahí la manejan Windows y los programas donde la pegues o abras; por ejemplo, si tienes activado el historial del portapapeles de Windows, Windows también la guarda.

## Cuánto tiempo se conserva

Las capturas se conservan hasta que las borras. Puedes:

- eliminar una o todas desde el historial o el panel de capturas recientes;
- elegir en Ajustes cuántas conservar (50 por defecto): las más viejas se borran solas;
- borrar la carpeta a mano.

Si desinstalas la versión de la Microsoft Store, Windows borra la carpeta de la aplicación con todo su contenido.

## Con quién se comparte

Con nadie. Popete no transmite tus capturas, anotaciones ni ajustes a ningún lugar.

## Inicio con Windows

Si activas **Iniciar con Windows**, Popete se registra para abrirse al iniciar sesión. Esto no recopila ningún dato. Lo puedes desactivar en Ajustes o en **Configuración › Aplicaciones › Inicio** de Windows.

## Microsoft Store

Si descargas Popete desde la Microsoft Store, Microsoft puede recopilar datos sobre la instalación y el funcionamiento de la aplicación (por ejemplo, informes de errores) según su propia [Declaración de privacidad](https://privacy.microsoft.com/). El desarrollador solo recibe de Microsoft informes agregados y anónimos, como el número de descargas o de errores, sin datos que te identifiquen ni el contenido de tus capturas.

## Menores de edad

Popete no recopila datos personales de nadie, incluidos menores de edad.

## Cambios en esta política

Si la aplicación cambia la forma en que usa los datos, se actualizará esta política y su fecha.

## Contacto

Para preguntas sobre esta política: [omarquiaybar@gmail.com](mailto:omarquiaybar@gmail.com)
