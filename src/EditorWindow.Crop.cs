// ----------------------------------------------------------------------------
//  Editor — recortar
//
//  Con la herramienta Recortar (C) se arrastra un rectángulo sobre la captura;
//  al soltar, lo visible pasa a ser ese rectángulo. original.png no se toca:
//  el recorte se guarda en meta.json (SessionMeta.Crop) y se puede quitar con
//  "Quitar recorte" en la barra de estado. Las anotaciones que quedan
//  totalmente fuera del recorte se borran; las que cruzan el borde se quedan
//  (y, como en el margen, agrandan la imagen al exportar). Todo se deshace
//  con Ctrl+Z en un solo paso.
//
//  La tabla de dibujo está anclada a la captura original, así que recortar no
//  mueve ninguna coordenada: solo cambian _crop, _image y LayoutForImage.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    Point? _cropStart;     // inicio del arrastre, en coordenadas de la tabla
    Rect _cropRect;        // rectángulo que se está marcando
    Button _uncropButton;

    /// <summary>El recorte guardado, si es válido para la captura original.</summary>
    Int32Rect? ValidCrop(int[] c)
    {
        if (c == null || c.Length != 4) return null;
        int w = _original.PixelWidth, h = _original.PixelHeight;
        if (c[0] < 0 || c[1] < 0 || c[2] < 1 || c[3] < 1 || c[0] + c[2] > w || c[1] + c[3] > h) return null;
        return new Int32Rect(c[0], c[1], c[2], c[3]);
    }

    bool IsCropped => _crop.X != 0 || _crop.Y != 0 || _crop.Width != _original.PixelWidth || _crop.Height != _original.PixelHeight;

    BitmapSource CropOf(Int32Rect r)
    {
        if (r.X == 0 && r.Y == 0 && r.Width == _original.PixelWidth && r.Height == _original.PixelHeight) return _original;
        var cropped = new CroppedBitmap(_original, r);
        cropped.Freeze();
        return cropped;
    }

    // ------------------------------------------------------------------ arrastre
    bool CropMouseDown(Point p)
    {
        if (_tool != Tool.Crop) return false;
        _cropStart = Clamp(p, VisibleRect);
        _cropRect = new Rect(_cropStart.Value, _cropStart.Value);
        _ink.CaptureMouse();
        DrawCropPreview();
        return true;
    }

    bool CropMouseMove(Point p)
    {
        if (_cropStart == null) return false;
        _cropRect = new Rect(_cropStart.Value, Clamp(p, VisibleRect));
        DrawCropPreview();
        return true;
    }

    bool CropMouseUp(Point p)
    {
        if (_cropStart == null) return false;
        _ink.ReleaseMouseCapture();
        _cropStart = null;
        _selectionLayer.Children.Clear();
        UpdateSelectionVisual();

        // Un clic o un rectángulo diminuto no recorta
        if (_cropRect.Width < 8 || _cropRect.Height < 8) return true;

        // A píxeles enteros de la captura original
        int x = (int)Math.Round(_cropRect.X - _pad), y = (int)Math.Round(_cropRect.Y - _pad);
        int w = (int)Math.Round(_cropRect.Width), h = (int)Math.Round(_cropRect.Height);
        w = Math.Min(w, _original.PixelWidth - x);
        h = Math.Min(h, _original.PixelHeight - y);
        ApplyCrop(new Int32Rect(x, y, w, h));
        return true;
    }

    static Point Clamp(Point p, Rect r) =>
        new Point(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));

    /// <summary>Oscurece lo que queda afuera y marca el rectángulo (en la capa de selección: no se exporta).</summary>
    void DrawCropPreview()
    {
        _selectionLayer.Children.Clear();
        double z = _zoom.ScaleX;
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(VisibleRect), new RectangleGeometry(_cropRect));
        _selectionLayer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = outside,
            Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
        });
        var frame = new System.Windows.Shapes.Rectangle
        {
            Width = _cropRect.Width,
            Height = _cropRect.Height,
            StrokeThickness = 1.5 / z,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };
        frame.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");
        Canvas.SetLeft(frame, _cropRect.X);
        Canvas.SetTop(frame, _cropRect.Y);
        _selectionLayer.Children.Add(frame);
    }

    // ------------------------------------------------------------------ aplicar
    void ApplyCrop(Int32Rect crop)
    {
        CommitText();
        ClearSelection();
        var before = _crop;
        var area = new Rect(_pad + crop.X, _pad + crop.Y, crop.Width, crop.Height);

        // Lo que queda totalmente afuera se borra junto con el recorte
        var strokesOut = new StrokeCollection(_ink.Strokes.Where(s => !s.GetBounds().IntersectsWith(area)));
        var textsOut = TextBlocks
            .Where(tb => !new Rect(Canvas.GetLeft(tb), Canvas.GetTop(tb), tb.ActualWidth, tb.ActualHeight).IntersectsWith(area))
            .Cast<UIElement>().ToList();

        void Do()
        {
            _ink.Strokes.Remove(strokesOut);
            foreach (var e in textsOut) _textLayer.Children.Remove(e);
            SetCrop(crop);
        }
        void Undo()
        {
            SetCrop(before);
            _ink.Strokes.Add(strokesOut);
            foreach (var e in textsOut) _textLayer.Children.Add(e);
        }

        _suppress = true;
        try { Do(); }
        finally { _suppress = false; }
        PushUndo(new ActionChange(Undo, Do));
    }

    void RemoveCrop()
    {
        if (!IsCropped) return;
        ApplyCrop(new Int32Rect(0, 0, _original.PixelWidth, _original.PixelHeight));
    }

    void SetCrop(Int32Rect crop)
    {
        _crop = crop;
        _image = CropOf(crop);
        _session.Meta.Crop = IsCropped ? new[] { crop.X, crop.Y, crop.Width, crop.Height } : null;
        LayoutForImage();
        if (_uncropButton != null) _uncropButton.Visibility = IsCropped ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.InvokeAsync(FitZoom, DispatcherPriority.Loaded); // encuadrar lo nuevo cuando se acomode
    }
}
