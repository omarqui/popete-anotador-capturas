// ----------------------------------------------------------------------------
//  Editor — exportar, copiar al portapapeles y guardar
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    // ------------------------------------------------------------------ exportar
    BitmapSource Render()
    {
        int w = _image.PixelWidth, h = _image.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new VisualBrush(_surface)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    bool CopyToClipboard()
    {
        CommitText();
        var bmp = Render();

        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(png);

        // Se registra la huella de lo que se copia para que la app no
        // vuelva a abrir su propia imagen exportada.
        var exportSig = ClipboardHelper.Signature(ClipboardHelper.Decode(new MemoryStream(png.ToArray())));
        History.RegisterExport(_session, exportSig);
        png.Position = 0;

        var data = new DataObject();
        data.SetImage(bmp);
        data.SetData("PNG", png, false);

        for (int intento = 0; intento < 5; intento++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                _status.Text = "✓ Imagen anotada copiada al portapapeles";
                FlashCopied();
                if (_pendingSave) SaveSession();
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        _status.Text = "No se pudo acceder al portapapeles. Intenta de nuevo.";
        return false;
    }

    /// <summary>Doble parpadeo blanco con borde azul sobre la imagen.</summary>
    void FlashCopied()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 0.75,
            Duration = TimeSpan.FromMilliseconds(110),
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        _flash.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Cierra el editor cuando termina el parpadeo, para que se vea que se copió.</summary>
    void CloseAfterFlash()
    {
        if (_closeScheduled) return;
        _closeScheduled = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        t.Tick += (s, e) =>
        {
            t.Stop();
            if (Open.ContainsKey(_session.Meta.Id)) Close(); // pudo cerrarse a mano mientras tanto
        };
        t.Start();
    }

    void Save()
    {
        CommitText();
        var dlg = new SaveFileDialog
        {
            Filter = "Imagen PNG (*.png)|*.png|Imagen JPEG (*.jpg)|*.jpg",
            FileName = $"Captura {DateTime.Now:yyyy-MM-dd HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;

        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        BitmapEncoder enc = ext is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render()));
        using (var fs = File.Create(dlg.FileName)) enc.Save(fs);

        _status.Text = $"✓ Guardado en {dlg.FileName}";
    }
}
