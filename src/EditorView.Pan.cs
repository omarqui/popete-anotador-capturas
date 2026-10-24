// ----------------------------------------------------------------------------
//  Editor — mover la vista con el clic central (pan)
//
//  Arrastrar con el botón central mueve la captura con una mano como cursor.
//  Primero se usa el desplazamiento del ScrollViewer; lo que no alcanza (o
//  todo, si la captura entra entera en la ventana) va a _panShift, una
//  traslación del host que no toca el layout. "Ajustar" la vuelve a cero.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    readonly TranslateTransform _panShift = new TranslateTransform();
    Point? _panStart;
    double _panFromX, _panFromY, _panFromShiftX, _panFromShiftY;

    void BeginPan(Point p)
    {
        _panStart = p;
        _panFromX = _scroll.HorizontalOffset;
        _panFromY = _scroll.VerticalOffset;
        _panFromShiftX = _panShift.X;
        _panFromShiftY = _panShift.Y;
        _scroll.CaptureMouse();
        Mouse.OverrideCursor = GrabCursor();
        _pointerLayer.Visibility = Visibility.Hidden; // el puntero propio quedaría quieto
    }

    bool PanMove(Point p)
    {
        if (_panStart is not Point start) return false;
        var (sx, tx) = SplitPan(_panFromX, _panFromShiftX, p.X - start.X, _scroll.ScrollableWidth, _scroll.ViewportWidth);
        var (sy, ty) = SplitPan(_panFromY, _panFromShiftY, p.Y - start.Y, _scroll.ScrollableHeight, _scroll.ViewportHeight);
        _scroll.ScrollToHorizontalOffset(sx);
        _scroll.ScrollToVerticalOffset(sy);
        _panShift.X = tx;
        _panShift.Y = ty;
        return true;
    }

    /// <summary>
    /// Reparte el arrastre en un eje: la posición buscada es −desplazamiento + traslación + delta.
    /// El desplazamiento toma todo lo que puede; el resto queda como traslación, que no deja
    /// sacar la captura de la vista más del 80 %.
    /// </summary>
    static (double scroll, double shift) SplitPan(double fromScroll, double fromShift, double delta, double scrollable, double viewport)
    {
        double target = -fromScroll + fromShift + delta;
        double scroll = Math.Clamp(-target, 0, Math.Max(0, scrollable));
        double limit = viewport * 0.8;
        return (scroll, Math.Clamp(target + scroll, -limit, limit));
    }

    void EndPan()
    {
        if (_panStart == null) return;
        _panStart = null;
        Mouse.OverrideCursor = null;
        _pointerLayer.Visibility = Visibility.Visible;
        if (_scroll.IsMouseCaptured) _scroll.ReleaseMouseCapture();
    }

    void ResetPanShift() => _panShift.X = _panShift.Y = 0;

    /// <summary>Si el elemento del evento (puede ser un Run, que no es Visual) está dentro de <paramref name="container"/>.</summary>
    static bool IsInside(object source, Visual container)
    {
        var d = source as DependencyObject;
        while (d != null && d is not Visual) d = LogicalTreeHelper.GetParent(d);
        return d != null && container.IsAncestorOf(d);
    }

    // ------------------------------------------------------------------ cursor de mano
    // WPF no trae una mano de agarrar (Cursors.Hand es el dedo de los enlaces): se dibuja
    // Icons.Grab en negro con halo blanco y se arma un .cur con el PNG adentro.
    static Cursor _grabCursor;
    static double _grabCursorDpi;

    Cursor GrabCursor()
    {
        double dpi = DpiScale();
        if (_grabCursor != null && _grabCursorDpi == dpi) return _grabCursor;
        try
        {
            int px = (int)Math.Round(32 * dpi);
            double scale = (px - 4) / 24.0; // 2 px de margen para el halo
            var geometry = Geometry.Parse(Icons.Grab);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(2, 2));
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(null, RoundPen(Brushes.White, 5 * dpi / scale), geometry);
                dc.DrawGeometry(null, RoundPen(Brushes.Black, 2 * dpi / scale), geometry);
            }
            var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(png);

            // .cur: ICONDIR (tipo 2 = cursor) + una entrada con el punto activo + el PNG
            var cur = new MemoryStream();
            var w = new BinaryWriter(cur);
            w.Write((short)0);
            w.Write((short)2);
            w.Write((short)1);
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)Math.Round(2 + 12 * scale)); // punto activo: la palma
            w.Write((short)Math.Round(2 + 14 * scale));
            w.Write((int)png.Length);
            w.Write(22);
            w.Write(png.ToArray());
            cur.Position = 0;

            _grabCursor = new Cursor(cur);
            _grabCursorDpi = dpi;
            return _grabCursor;
        }
        catch
        {
            return Cursors.SizeAll;
        }
    }

    static Pen RoundPen(Brush brush, double thickness) => new Pen(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };
}
