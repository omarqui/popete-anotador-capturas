// ----------------------------------------------------------------------------
//  Editor — recortar
//
//  Con la herramienta Recortar (C) se ve la captura original entera con un
//  marco encima: se arrastran sus bordes o esquinas hacia adentro (o hacia
//  afuera, hasta la captura completa), y arrastrar desde adentro mueve el marco.
//  Lo que queda afuera se oscurece. El recorte se aplica al salir de la
//  herramienta o con Enter; Esc lo cancela.
//
//  original.png no se toca: el recorte se guarda en meta.json (SessionMeta.Crop)
//  y se puede quitar con "Quitar recorte" en la barra de estado. Las anotaciones
//  que quedan totalmente fuera del recorte se borran; las que cruzan el borde se
//  quedan (y, como en el margen, agrandan la imagen al exportar). Todo se deshace
//  con Ctrl+Z en un solo paso.
//
//  La tabla de dibujo está anclada a la captura original, así que recortar no
//  mueve ninguna coordenada: solo cambian _crop, _image y LayoutForImage.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    [Flags]
    enum CropEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16 }

    bool _cropMode;                // herramienta Recortar activa: se ve la captura original entera
    Rect _pendingCrop;             // marco que se está ajustando, en coordenadas de la tabla
    Tool _toolBeforeCrop = Tool.Pen;
    CropEdge _cropDrag;            // qué se está arrastrando
    Point _cropDragStart;
    Rect _cropDragFrom;
    Button _uncropButton;

    const double MinCrop = 8;

    /// <summary>El recorte guardado, si es válido para la captura original.</summary>
    Int32Rect? ValidCrop(int[] c)
    {
        if (c == null || c.Length != 4) return null;
        int w = _original.PixelWidth, h = _original.PixelHeight;
        if (c[0] < 0 || c[1] < 0 || c[2] < 1 || c[3] < 1 || c[0] + c[2] > w || c[1] + c[3] > h) return null;
        return new Int32Rect(c[0], c[1], c[2], c[3]);
    }

    bool IsCropped => _crop.X != 0 || _crop.Y != 0 || _crop.Width != _original.PixelWidth || _crop.Height != _original.PixelHeight;

    /// <summary>La captura original completa, en coordenadas de la tabla.</summary>
    Rect OriginalRect => new Rect(_pad, _pad, _original.PixelWidth, _original.PixelHeight);

    /// <summary>Lo que se muestra: la captura entera mientras se recorta; si no, el recorte.</summary>
    Rect ShownRect => _cropMode ? OriginalRect : VisibleRect;
    BitmapSource ShownImage => _cropMode ? _original : _image;

    BitmapSource CropOf(Int32Rect r)
    {
        if (r.X == 0 && r.Y == 0 && r.Width == _original.PixelWidth && r.Height == _original.PixelHeight) return _original;
        var cropped = new CroppedBitmap(_original, r);
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Marco de la tabla → píxeles enteros de la captura original.</summary>
    Int32Rect ToCropRect(Rect r)
    {
        int x = (int)Math.Round(r.X - _pad), y = (int)Math.Round(r.Y - _pad);
        x = Math.Clamp(x, 0, _original.PixelWidth - 1);
        y = Math.Clamp(y, 0, _original.PixelHeight - 1);
        int w = Math.Clamp((int)Math.Round(r.Width), 1, _original.PixelWidth - x);
        int h = Math.Clamp((int)Math.Round(r.Height), 1, _original.PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    // ------------------------------------------------------------------ entrar y salir
    /// <summary>Lo llama SetTool al elegir Recortar.</summary>
    void BeginCropMode(Tool previous)
    {
        _toolBeforeCrop = previous == Tool.Crop ? Tool.Pen : previous;
        _cropMode = true;
        _pendingCrop = VisibleRect;
        LayoutForImage();
        Dispatcher.InvokeAsync(FitZoom, DispatcherPriority.Loaded); // encuadrar la captura entera
        UpdateSelectionVisual();
        _status.Text = "Arrastra los bordes o las esquinas · Enter aplica · Esc cancela";
    }

    /// <summary>Lo llama SetTool al dejar Recortar: aplica el marco si cambió.</summary>
    void EndCropMode()
    {
        _cropMode = false;
        _cropDrag = CropEdge.None;
        _status.Text = "";
        var target = ToCropRect(_pendingCrop);
        if (target != _crop) ApplyCrop(target);
        else
        {
            LayoutForImage();
            Dispatcher.InvokeAsync(FitZoom, DispatcherPriority.Loaded);
        }
        UpdateSelectionVisual();
    }

    /// <summary>Enter: aplica y vuelve a la herramienta anterior.</summary>
    void ConfirmCrop() => SetTool(_toolBeforeCrop);

    /// <summary>Esc: descarta el marco y vuelve a la herramienta anterior.</summary>
    void CancelCrop()
    {
        _pendingCrop = VisibleRect;
        SetTool(_toolBeforeCrop);
    }

    /// <summary>Ctrl+Z con el marco movido: primero lo devuelve a su lugar.</summary>
    bool ResetPendingCrop()
    {
        if (!_cropMode || ToCropRect(_pendingCrop) == _crop) return false;
        _pendingCrop = VisibleRect;
        UpdateSelectionVisual();
        return true;
    }

    /// <summary>Después de deshacer o rehacer con la herramienta activa: el marco sigue al recorte.</summary>
    void SyncPendingCrop()
    {
        if (!_cropMode) return;
        _pendingCrop = VisibleRect;
        LayoutForImage();
        UpdateSelectionVisual();
    }

    // ------------------------------------------------------------------ arrastre
    CropEdge CropEdgeAt(Point p)
    {
        double tol = 10 / _zoom.ScaleX;
        var r = _pendingCrop;
        bool inX = p.X >= r.Left - tol && p.X <= r.Right + tol;
        bool inY = p.Y >= r.Top - tol && p.Y <= r.Bottom + tol;
        var edge = CropEdge.None;
        if (inY)
        {
            if (Math.Abs(p.X - r.Left) <= tol) edge |= CropEdge.Left;
            else if (Math.Abs(p.X - r.Right) <= tol) edge |= CropEdge.Right;
        }
        if (inX)
        {
            if (Math.Abs(p.Y - r.Top) <= tol) edge |= CropEdge.Top;
            else if (Math.Abs(p.Y - r.Bottom) <= tol) edge |= CropEdge.Bottom;
        }
        if (edge == CropEdge.None && r.Contains(p)) edge = CropEdge.Move;
        return edge;
    }

    static Cursor CursorFor(CropEdge e) => e switch
    {
        CropEdge.Left or CropEdge.Right => Cursors.SizeWE,
        CropEdge.Top or CropEdge.Bottom => Cursors.SizeNS,
        CropEdge.Left | CropEdge.Top or CropEdge.Right | CropEdge.Bottom => Cursors.SizeNWSE,
        CropEdge.Right | CropEdge.Top or CropEdge.Left | CropEdge.Bottom => Cursors.SizeNESW,
        CropEdge.Move => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };

    bool CropMouseDown(Point p)
    {
        if (!_cropMode) return false;
        _cropDrag = CropEdgeAt(p);
        if (_cropDrag == CropEdge.None) return true; // fuera del marco: nada
        _cropDragStart = p;
        _cropDragFrom = _pendingCrop;
        _ink.CaptureMouse();
        UpdateSelectionVisual(); // muestra la guía de tercios
        return true;
    }

    bool CropMouseMove(Point p)
    {
        if (!_cropMode) return false;
        if (_cropDrag == CropEdge.None)
        {
            _ink.Cursor = CursorFor(CropEdgeAt(p)); // indica qué se puede arrastrar
            return true;
        }

        var d = p - _cropDragStart;
        var r = _cropDragFrom;
        var lim = OriginalRect;
        if (_cropDrag == CropEdge.Move)
        {
            double x = Math.Clamp(r.X + d.X, lim.Left, lim.Right - r.Width);
            double y = Math.Clamp(r.Y + d.Y, lim.Top, lim.Bottom - r.Height);
            _pendingCrop = new Rect(x, y, r.Width, r.Height);
        }
        else
        {
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (_cropDrag.HasFlag(CropEdge.Left)) left = Math.Clamp(r.Left + d.X, lim.Left, right - MinCrop);
            if (_cropDrag.HasFlag(CropEdge.Right)) right = Math.Clamp(r.Right + d.X, left + MinCrop, lim.Right);
            if (_cropDrag.HasFlag(CropEdge.Top)) top = Math.Clamp(r.Top + d.Y, lim.Top, bottom - MinCrop);
            if (_cropDrag.HasFlag(CropEdge.Bottom)) bottom = Math.Clamp(r.Bottom + d.Y, top + MinCrop, lim.Bottom);
            _pendingCrop = new Rect(new Point(left, top), new Point(right, bottom));
        }
        UpdateSelectionVisual();
        return true;
    }

    bool CropMouseUp(Point p)
    {
        if (!_cropMode) return false;
        if (_cropDrag != CropEdge.None)
        {
            _cropDrag = CropEdge.None;
            _ink.ReleaseMouseCapture();
            UpdateSelectionVisual();
        }
        return true;
    }

    /// <summary>
    /// Oscurece lo que queda afuera y dibuja el marco con esquinas y asas en los lados
    /// (en la capa de selección: no se exporta). Lo llama UpdateSelectionVisual.
    /// </summary>
    void AddCropOverlay()
    {
        double z = _zoom.ScaleX;
        var r = _pendingCrop;

        _selectionLayer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(OriginalRect), new RectangleGeometry(r)),
            Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
        });

        var lines = new GeometryGroup();
        lines.Children.Add(new RectangleGeometry(r));
        if (_cropDrag != CropEdge.None)
        {
            // Guía de tercios mientras se arrastra
            for (int i = 1; i <= 2; i++)
            {
                double x = r.Left + r.Width * i / 3, y = r.Top + r.Height * i / 3;
                lines.Children.Add(new LineGeometry(new Point(x, r.Top), new Point(x, r.Bottom)));
                lines.Children.Add(new LineGeometry(new Point(r.Left, y), new Point(r.Right, y)));
            }
        }
        _selectionLayer.Children.Add(new System.Windows.Shapes.Path
        {
            Data = lines,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1 / z,
        });

        // Esquinas en L y asas en el medio de cada lado
        double len = Math.Min(22 / z, Math.Min(r.Width, r.Height) / 3);
        double cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        var handles = new GeometryGroup();
        void Line(double x1, double y1, double x2, double y2) =>
            handles.Children.Add(new LineGeometry(new Point(x1, y1), new Point(x2, y2)));
        Line(r.Left, r.Top + len, r.Left, r.Top); Line(r.Left, r.Top, r.Left + len, r.Top);
        Line(r.Right - len, r.Top, r.Right, r.Top); Line(r.Right, r.Top, r.Right, r.Top + len);
        Line(r.Right, r.Bottom - len, r.Right, r.Bottom); Line(r.Right, r.Bottom, r.Right - len, r.Bottom);
        Line(r.Left + len, r.Bottom, r.Left, r.Bottom); Line(r.Left, r.Bottom, r.Left, r.Bottom - len);
        Line(cx - len / 2, r.Top, cx + len / 2, r.Top);
        Line(cx - len / 2, r.Bottom, cx + len / 2, r.Bottom);
        Line(r.Left, cy - len / 2, r.Left, cy + len / 2);
        Line(r.Right, cy - len / 2, r.Right, cy + len / 2);
        foreach (var (brush, thickness) in new (Brush, double)[]
        {
            (new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 6.5),  // contorno oscuro: se ve sobre fondos claros
            (Brushes.White, 4),
        })
            _selectionLayer.Children.Add(new System.Windows.Shapes.Path
            {
                Data = handles,
                Stroke = brush,
                StrokeThickness = thickness / z,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
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
        if (_cropMode)
        {
            // Con la herramienta activa, solo abre el marco a la captura entera (Enter lo aplica)
            _pendingCrop = OriginalRect;
            UpdateSelectionVisual();
            return;
        }
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
