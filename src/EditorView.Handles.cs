// ----------------------------------------------------------------------------
//  Editor — asas para reajustar un trazo
//
//  Con un solo trazo elegido a mano (herramienta Seleccionar o clic en su número; no
//  la selección automática de lo recién dibujado, para no agarrar un asa al empezar
//  el trazo siguiente) aparecen asas según su forma:
//   - línea: una en cada extremo (Shift: 45° respecto del otro);
//   - flecha, recta o curva: inicio y punta (el recorrido gira y se estira desde el
//     otro extremo, MovedArrow) y una en el medio que la dobla (BentArrow; una recta
//     pasa a ser curva); la cabeza se rehace;
//   - rectángulo y óvalo: esquinas y lados; la forma se rehace con ShapePoints;
//   - lápiz, resaltador y trazos de capturas viejas: esquinas y lados que escalan
//     sus puntos sin cambiar el grosor.
//  El tipo de forma se guarda en el trazo al dibujarla (ShapeProperty). Las asas
//  van en _selectionLayer (no se exportan) y funcionan con cualquier herramienta.
//  Cada arrastre es un paso de Ctrl+Z.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Tipo de forma (propiedad del ISF: el valor de <see cref="Tool"/>).</summary>
    static readonly Guid ShapeProperty = new Guid("e7b5c9a1-3d2f-4c6e-8a0b-5f1d7e9c2a46");

    /// <summary>
    /// Un extremo (<c>End</c> 0 o 1), el medio de una flecha (<c>End</c> 2) o un lado/esquina
    /// del recuadro (<c>End</c> -1 y <c>Edge</c>).
    /// </summary>
    readonly record struct Handle(Point At, int End, CropEdge Edge);

    readonly List<Handle> _handles = new List<Handle>();
    Handle? _handleDrag;
    Stroke _handleStroke;
    Tool? _handleKind;
    int? _handleTip;
    StylusPointCollection _handleBefore;
    Rect _handleFrom;
    Point _handleStart;
    bool _overHandle;

    /// <summary>La forma guardada, si sus puntos siguen siendo de esa forma; si no, null (trazo libre).</summary>
    static Tool? ShapeOf(Stroke s)
    {
        if (!s.ContainsPropertyData(ShapeProperty)) return null;
        var kind = (Tool)Convert.ToInt32(s.GetPropertyData(ShapeProperty));
        int n = s.StylusPoints.Count;
        return kind switch
        {
            Tool.Line when n == 2 => kind,
            Tool.Arrow when n >= 2 => kind,
            Tool.Rectangle when n == 5 => kind,
            Tool.Ellipse when n >= 24 => kind,
            _ => null,
        };
    }

    /// <summary>
    /// Índice de la punta de una flecha: el recorrido va hasta ahí y la cabeza (AddArrowHead)
    /// termina otra vez en ella. Sirve también para flechas curvas y de capturas viejas, que
    /// no guardan su forma. Null si no es una flecha.
    /// </summary>
    static int? ArrowTip(Stroke s)
    {
        if (!s.ContainsPropertyData(ArrowProperty)) return null;
        var pts = s.StylusPoints;
        var tip = pts[pts.Count - 1].ToPoint();
        for (int i = 1; i < pts.Count; i++)
            if ((pts[i].ToPoint() - tip).Length < 1e-3) return i;
        return null;
    }

    /// <summary>
    /// La flecha con el extremo <paramref name="end"/> (0 inicio, 1 punta) llevado a <paramref name="to"/>:
    /// el recorrido gira y se estira alrededor del otro extremo (así una curva conserva su forma)
    /// y la cabeza se rehace con el grosor actual.
    /// </summary>
    static StylusPointCollection MovedArrow(StylusPointCollection pts, int tip, int end, Point to, double size)
    {
        var body = pts.Take(tip + 1).Select(p => p.ToPoint()).ToList();
        var pivot = end == 0 ? body[^1] : body[0];
        var v = (end == 0 ? body[0] : body[^1]) - pivot;
        var w = to - pivot;
        if (v.Length < 1e-6 || w.Length < 1e-6) return pts.Clone();
        var m = new Matrix();
        m.Translate(-pivot.X, -pivot.Y);
        m.Scale(w.Length / v.Length, w.Length / v.Length);
        m.Rotate(Vector.AngleBetween(v, w));
        m.Translate(pivot.X, pivot.Y);
        return ArrowAlong(body.Select(m.Transform).ToList(), size);
    }

    /// <summary>El punto a mitad del recorrido de la flecha (por largo, no por cantidad de puntos).</summary>
    static Point ArrowMiddle(StylusPointCollection pts, int tip)
    {
        var body = pts.Take(tip + 1).Select(p => p.ToPoint()).ToList();
        double half = PathLength(body) / 2, walked = 0;
        for (int i = 1; i < body.Count; i++)
        {
            double step = (body[i] - body[i - 1]).Length;
            if (step > 0 && walked + step >= half) return body[i - 1] + (body[i] - body[i - 1]) * ((half - walked) / step);
            walked += step;
        }
        return body[0];
    }

    /// <summary>
    /// La flecha doblada: el medio del recorrido se corre <paramref name="d"/> y cada punto lo
    /// acompaña según el seno de lo recorrido (los extremos no se mueven), así no quedan quiebres.
    /// Una recta tiene dos puntos: antes se reparte en tramos de ~4 px para que pueda curvarse.
    /// </summary>
    static StylusPointCollection BentArrow(StylusPointCollection pts, int tip, Vector d, double size)
    {
        var body = pts.Take(tip + 1).Select(p => p.ToPoint()).ToList();
        double total = PathLength(body);
        if (total < 1) return pts.Clone();
        if (body.Count < 24) body = Resample(body, Math.Clamp((int)(total / 4), 24, 200));
        var bent = new List<Point>(body.Count);
        double walked = 0;
        for (int i = 0; i < body.Count; i++)
        {
            if (i > 0) walked += (body[i] - body[i - 1]).Length;
            bent.Add(body[i] + d * Math.Sin(Math.PI * walked / total));
        }
        bent[^1] = body[^1]; // la punta exacta: la cabeza termina en ella (ArrowTip)
        return ArrowAlong(bent, size);
    }

    /// <summary><paramref name="n"/> + 1 puntos repartidos parejo a lo largo del recorrido.</summary>
    static List<Point> Resample(List<Point> path, int n)
    {
        double total = PathLength(path), start = 0;
        var result = new List<Point> { path[0] };
        int seg = 1;
        for (int k = 1; k < n; k++)
        {
            double target = total * k / n;
            while (seg < path.Count - 1 && start + (path[seg] - path[seg - 1]).Length < target)
            {
                start += (path[seg] - path[seg - 1]).Length;
                seg++;
            }
            double len = (path[seg] - path[seg - 1]).Length;
            result.Add(len > 0 ? path[seg - 1] + (path[seg] - path[seg - 1]) * ((target - start) / len) : path[seg]);
        }
        result.Add(path[^1]);
        return result;
    }

    static Rect PointBounds(StylusPointCollection pts)
    {
        var r = Rect.Empty;
        foreach (var p in pts) r.Union(p.ToPoint());
        return r;
    }

    Stroke EditableStroke =>
        _selection.Count == 1 && _selectedTexts.Count == 0 && !_selectionAuto && !_cropMode
        && _ink.Strokes.Contains(_selection[0]) ? _selection[0] : null;

    /// <summary>Calcula y dibuja las asas del trazo editable. Lo llama UpdateSelectionVisual.</summary>
    void AddHandles()
    {
        _handles.Clear();
        var s = _handleStroke ?? EditableStroke;
        if (s == null) return;

        int? tip = ArrowTip(s);
        if (tip != null || ShapeOf(s) == Tool.Line)
        {
            _handles.Add(new Handle(s.StylusPoints[0].ToPoint(), 0, CropEdge.None));
            _handles.Add(new Handle(s.StylusPoints[tip ?? 1].ToPoint(), 1, CropEdge.None));
            if (tip is int t) _handles.Add(new Handle(ArrowMiddle(s.StylusPoints, t), 2, CropEdge.None));
        }
        else
        {
            var r = PointBounds(s.StylusPoints);
            // Un trazo casi plano (una raya a mano) solo se estira a lo largo
            bool wide = r.Width * _zoom.ScaleX >= 4, tall = r.Height * _zoom.ScaleX >= 4;
            var edges = new List<CropEdge>();
            if (wide && tall)
                edges.AddRange(new[] { CropEdge.Left | CropEdge.Top, CropEdge.Right | CropEdge.Top, CropEdge.Right | CropEdge.Bottom, CropEdge.Left | CropEdge.Bottom });
            if (wide) edges.AddRange(new[] { CropEdge.Left, CropEdge.Right });
            if (tall) edges.AddRange(new[] { CropEdge.Top, CropEdge.Bottom });
            foreach (var e in edges) _handles.Add(new Handle(EdgePoint(r, e), -1, e));
        }

        double z = _zoom.ScaleX, size = 10 / z;
        foreach (var h in _handles)
        {
            System.Windows.Shapes.Shape mark = h.End >= 0
                ? new System.Windows.Shapes.Ellipse()
                : new System.Windows.Shapes.Rectangle { RadiusX = 2 / z, RadiusY = 2 / z };
            mark.Width = mark.Height = size;
            mark.Fill = Brushes.White;
            mark.StrokeThickness = 1.5 / z;
            mark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");
            Canvas.SetLeft(mark, h.At.X - size / 2);
            Canvas.SetTop(mark, h.At.Y - size / 2);
            _selectionLayer.Children.Add(mark);
        }
    }

    static Point EdgePoint(Rect r, CropEdge e) => new Point(
        e.HasFlag(CropEdge.Left) ? r.Left : e.HasFlag(CropEdge.Right) ? r.Right : (r.Left + r.Right) / 2,
        e.HasFlag(CropEdge.Top) ? r.Top : e.HasFlag(CropEdge.Bottom) ? r.Bottom : (r.Top + r.Bottom) / 2);

    /// <summary>El asa más cercana a <paramref name="p"/> (8 px en pantalla), o null.</summary>
    Handle? HandleAt(Point p)
    {
        double tolerance = 8 / _zoom.ScaleX;
        Handle? best = null;
        double bestDistance = tolerance;
        foreach (var h in _handles)
        {
            double d = (h.At - p).Length;
            if (d <= bestDistance) { best = h; bestDistance = d; }
        }
        return best;
    }

    bool HandleDown(Point p)
    {
        if (_handles.Count == 0 || EditableStroke is not Stroke s || HandleAt(p) is not Handle h) return false;
        CommitText();
        _handleDrag = h;
        _handleStroke = s;
        _handleKind = ShapeOf(s);
        _handleTip = ArrowTip(s);
        _handleBefore = s.StylusPoints.Clone();
        _handleFrom = PointBounds(_handleBefore);
        _handleStart = p;
        _ink.EditingMode = InkCanvasEditingMode.None; // que el lápiz no empiece un trazo
        _ink.CaptureMouse();
        return true;
    }

    /// <summary>Arrastre de un asa; sin arrastre, solo cambia el cursor sobre las asas (y devuelve false).</summary>
    bool HandleMove(Point p)
    {
        if (_handleDrag is not Handle h)
        {
            if (_handles.Count > 0 && HandleAt(p) is Handle over)
            {
                _ink.Cursor = over.End >= 0 ? Cursors.SizeAll : CursorFor(over.Edge);
                _pointerLayer.Visibility = Visibility.Hidden;
                _overHandle = true;
            }
            else if (_overHandle)
            {
                _overHandle = false;
                _ink.Cursor = ToolCursor;
                _pointerLayer.Visibility = Visibility.Visible;
            }
            return false;
        }

        var s = _handleStroke;
        var d = p - _handleStart;
        double width = s.DrawingAttributes.Width;
        if (h.End == 2)
            s.StylusPoints = BentArrow(_handleBefore, _handleTip.Value, d, width);
        else if (h.End >= 0)
        {
            var a = _handleBefore[0].ToPoint();
            var b = _handleBefore[_handleTip ?? 1].ToPoint();
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (h.End == 0) a = shift ? Snap45(b, a + d) : a + d;
            else b = shift ? Snap45(a, b + d) : b + d;
            s.StylusPoints = _handleTip is int tip
                ? MovedArrow(_handleBefore, tip, h.End, h.End == 0 ? a : b, width)
                : ShapePoints(Tool.Line, a, b, width);
        }
        else
        {
            var f = _handleFrom;
            double min = 4 / _zoom.ScaleX;
            double left = f.Left, top = f.Top, right = f.Right, bottom = f.Bottom;
            if (h.Edge.HasFlag(CropEdge.Left)) left = Math.Min(f.Left + d.X, right - min);
            if (h.Edge.HasFlag(CropEdge.Right)) right = Math.Max(f.Right + d.X, left + min);
            if (h.Edge.HasFlag(CropEdge.Top)) top = Math.Min(f.Top + d.Y, bottom - min);
            if (h.Edge.HasFlag(CropEdge.Bottom)) bottom = Math.Max(f.Bottom + d.Y, top + min);
            var r = new Rect(new Point(left, top), new Point(right, bottom));
            s.StylusPoints = _handleKind is Tool.Rectangle or Tool.Ellipse
                ? ShapePoints(_handleKind.Value, r.TopLeft, r.BottomRight, width)
                : Scaled(_handleBefore, f, r);
        }
        UpdateSelectionVisual();
        RefreshNumbers(); // el número sigue al punto de arriba a la izquierda
        return true;
    }

    bool HandleUp()
    {
        if (_handleDrag == null) return false;
        var s = _handleStroke;
        var before = _handleBefore;
        var after = s.StylusPoints.Clone();
        _handleDrag = null;
        _handleStroke = null;
        _handleBefore = null;
        _ink.ReleaseMouseCapture();
        _ink.EditingMode = EditingModeFor(_tool);
        if (!before.SequenceEqual(after))
            PushUndo(new ActionChange(() => s.StylusPoints = before.Clone(), () => s.StylusPoints = after.Clone()));
        UpdateSelectionVisual();
        return true;
    }

    /// <summary>Los puntos llevados del recuadro <paramref name="from"/> al <paramref name="to"/>.</summary>
    static StylusPointCollection Scaled(StylusPointCollection pts, Rect from, Rect to)
    {
        double sx = from.Width >= 1 ? to.Width / from.Width : 1;
        double sy = from.Height >= 1 ? to.Height / from.Height : 1;
        var result = pts.Clone();
        for (int i = 0; i < result.Count; i++)
        {
            var p = result[i];
            p.X = to.X + (p.X - from.X) * sx;
            p.Y = to.Y + (p.Y - from.Y) * sy;
            result[i] = p;
        }
        return result;
    }
}
