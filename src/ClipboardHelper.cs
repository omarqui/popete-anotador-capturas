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

                // 1) Formato PNG (Herramienta Recortes lo publica; conserva transparencia)
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
                    return Decode(png);

                // 2) Bitmap clásico (Impr Pant). Se pasa por PNG para evitar el
                //    problema conocido de WPF con el canal alfa de CF_DIB.
                if (data.GetDataPresent(System.Windows.Forms.DataFormats.Bitmap))
                {
                    using var image = System.Windows.Forms.Clipboard.GetImage();
                    if (image == null) return null;
                    var ms = new MemoryStream();
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return Decode(ms);
                }
                return null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return null;
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
