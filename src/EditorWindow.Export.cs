// ----------------------------------------------------------------------------
//  Editor — exportar, copiar al portapapeles y guardar
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    // ------------------------------------------------------------------ exportar
    /// <summary>Espacio extra alrededor de lo que sale de la captura, para que no quede pegado al borde.</summary>
    const double ExportBreathing = 12;

    /// <summary>
    /// Zona a exportar, en coordenadas de la tabla: la captura, agrandada lo justo
    /// para incluir trazos y textos que salen de ella.
    /// </summary>
    Rect ExportBounds()
    {
        var capture = VisibleRect;
        var all = capture;
        if (_ink.Strokes.Count > 0) all.Union(_ink.Strokes.GetBounds());
        foreach (var tb in _textLayer.Children.OfType<TextBlock>())
            all.Union(new Rect(Canvas.GetLeft(tb), Canvas.GetTop(tb), tb.ActualWidth, tb.ActualHeight));
        foreach (FrameworkElement badge in _numbersLayer.Children)
            all.Union(new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), badge.Width, badge.Height));

        double left = all.Left < capture.Left ? all.Left - ExportBreathing : capture.Left;
        double top = all.Top < capture.Top ? all.Top - ExportBreathing : capture.Top;
        double right = all.Right > capture.Right ? all.Right + ExportBreathing : capture.Right;
        double bottom = all.Bottom > capture.Bottom ? all.Bottom + ExportBreathing : capture.Bottom;

        // Enteros (para no partir píxeles de la captura) y dentro de la tabla
        left = Math.Max(0, Math.Floor(left));
        top = Math.Max(0, Math.Floor(top));
        right = Math.Min(_surface.Width, Math.Ceiling(right));
        bottom = Math.Min(_surface.Height, Math.Ceiling(bottom));
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Imagen final: la captura original (sin las esquinas redondeadas del editor) y
    /// las anotaciones encima. Si algo sale de la captura, el espacio nuevo es blanco.
    /// </summary>
    BitmapSource Render()
    {
        var b = ExportBounds();
        int w = (int)b.Width, h = (int)b.Height;
        bool expanded = w != _image.PixelWidth || h != _image.PixelHeight;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (expanded) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            var v = VisibleRect;
            dc.DrawImage(_image, new Rect(v.X - b.X, v.Y - b.Y, v.Width, v.Height));
            var annotations = new VisualBrush(_surface)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = b,
            };
            dc.DrawRectangle(annotations, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    bool CopyToClipboard()
    {
        CommitText();
        if (_cropMode) ConfirmCrop(); // copiar con el marco ajustado: se aplica primero
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
        MarkTouched(); // el diálogo de Windows le quita el foco: que no se cierre detrás
        CommitText();
        if (_cropMode) ConfirmCrop();
        var dlg = new SaveFileDialog
        {
            Filter = "Imagen PNG (*.png)|*.png|Imagen JPEG (*.jpg)|*.jpg",
            FileName = $"Captura {DateTime.Now:yyyy-MM-dd HHmmss}.png",
            InitialDirectory = AppSettings.Current.CarpetaGuardarEfectiva,
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
