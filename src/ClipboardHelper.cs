// ----------------------------------------------------------------------------
//  Lectura robusta de imágenes del portapapeles
// ----------------------------------------------------------------------------
static class ClipboardHelper
{
    public static BitmapSource TryGetImage()
    {
        // El portapapeles puede estar bloqueado por otra app: se reintenta.
        for (int intento = 0; intento < 5; intento++)
        {
            try
            {
                var data = System.Windows.Forms.Clipboard.GetDataObject();
                if (data == null) return null;

                // 1) Formato PNG (Herramienta Recortes lo publica; conserva transparencia).
                //    Hay programas que publican "PNG" con datos que no son un PNG válido:
                //    si no se puede leer, se prueba con el bitmap.
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png
                    && TryDecode(png) is BitmapSource fromPng)
                    return fromPng;

                // 2) Bitmap clásico (Impr Pant). Se pasa por PNG para evitar el
                //    problema conocido de WPF con el canal alfa de CF_DIB.
                if (data.GetDataPresent(System.Windows.Forms.DataFormats.Bitmap))
                {
                    using var image = System.Windows.Forms.Clipboard.GetImage();
                    if (image == null) return null;
                    var ms = new MemoryStream();
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return TryDecode(ms);
                }
                return null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100); // portapapeles bloqueado por otra app
            }
            catch (Exception)
            {
                return null; // lo que otro programa deje en el portapapeles nunca debe tumbar la app
            }
        }
        return null;
    }

    /// <summary>Como <see cref="Decode"/>, pero null si los datos no son una imagen que Windows sepa leer.</summary>
    public static BitmapSource TryDecode(Stream s)
    {
        try { return Decode(s); }
        catch (Exception) { return null; } // NotSupportedException, FileFormatException, COMException...
    }

    public static BitmapSource Decode(Stream s)
    {
        s.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = s;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>
    /// Pone un PNG en el portapapeles (como "PNG" y como bitmap). Antes registra su huella en
    /// <paramref name="session"/>, para que la app no tome la imagen por una captura nueva.
    /// Devuelve false si otra app no suelta el portapapeles.
    /// </summary>
    public static bool SetPng(byte[] png, Session session)
    {
        // La huella sale de decodificar el mismo PNG que va al portapapeles: es lo que se leerá después
        var bmp = Decode(new MemoryStream(png));
        History.RegisterExport(session, Signature(bmp));

        var data = new DataObject();
        data.SetImage(bmp);
        data.SetData("PNG", new MemoryStream(png), false);
        for (int intento = 0; intento < 5; intento++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100); // portapapeles bloqueado por otra app
            }
        }
        return false;
    }

    /// <summary>Huella de la imagen completa (tamaño + SHA-256 de los píxeles).</summary>
    public static string Signature(BitmapSource img)
    {
        var src = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var buffer = new byte[stride * h];
        src.CopyPixels(buffer, stride, 0);
        return $"{w}x{h}:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer));
    }
}
