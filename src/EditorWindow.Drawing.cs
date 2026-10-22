// ----------------------------------------------------------------------------
//  Editor — formas (línea, flecha, rectángulo) y textos
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    List<Point> _arrowPath;   // recorrido de la flecha mientras se dibuja (a mano alzada)

    // ------------------------------------------------------------------ formas (línea, flecha, rectángulo) y texto
    void Ink_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Select)
        {
            SelectToolDown(e.GetPosition(_ink)); // un trazo (y arrastrar lo mueve) o un recuadro
            e.Handled = true;
            return;
        }
        ClearSelection(); // empieza algo nuevo: lo anterior deja de estar seleccionado

        if (CropMouseDown(e.GetPosition(_ink)))
        {
            e.Handled = true;
            return;
        }

        if (_tool == Tool.Text)
        {
            CommitText();
            BeginText(e.GetPosition(_textLayer));
            e.Handled = true;
            return;
        }
        if (_tool is not (Tool.Line or Tool.Arrow or Tool.Rectangle)) return;

        _start = e.GetPosition(_ink);
        _arrowPath = _tool == Tool.Arrow ? new List<Point> { _start } : null;
        _preview = new NoteStroke(BuildPoints(_start, _start), MakeAttributes());
        _suppress = true;
        _ink.Strokes.Add(_preview);
        _suppress = false;
        _ink.CaptureMouse();
        e.Handled = true;
    }

    void Ink_MouseMove(object sender, MouseEventArgs e)
    {
        if (DragMove(e.GetPosition(_ink))) return;
        if (MarqueeMove(e.GetPosition(_ink))) return;
        if (CropMouseMove(e.GetPosition(_ink))) return;
        if (_preview == null) return;
        var p = e.GetPosition(_ink);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (_arrowPath != null)
        {
            // La flecha sigue el mouse (puntos cada 2 px en pantalla); con Shift, recta a 45°
            if ((p - _arrowPath[^1]).Length * _zoom.ScaleX >= 2) _arrowPath.Add(p);
            _preview.StylusPoints = shift ? BuildPoints(_start, Constrain(_start, p)) : FreeArrow(_arrowPath);
            return;
        }
        if (shift) p = Constrain(_start, p);
        _preview.StylusPoints = BuildPoints(_start, p);
    }

    void Ink_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DragEnd() || MarqueeEnd(e.GetPosition(_ink)))
        {
            e.Handled = true;
            return;
        }
        if (CropMouseUp(e.GetPosition(_ink)))
        {
            e.Handled = true;
            return;
        }
        if (_preview == null) return;
        _ink.ReleaseMouseCapture();
        var stroke = _preview;
        _preview = null;
        var path = _arrowPath;
        _arrowPath = null;

        var end = e.GetPosition(_ink);
        if (path != null && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if ((end - path[^1]).Length > 0) path.Add(end);
            // Casi recta: se endereza; si no, queda la curva suavizada
            stroke.StylusPoints = IsNearlyStraight(path) ? BuildPoints(_start, end) : FreeArrow(path);
        }

        double extent = path != null ? PathLength(path) : (end - _start).Length;
        if (extent * _zoom.ScaleX < 5)
        {
            // Un clic o un arrastre mínimo: se descarta (como en IsTiny)
            _suppress = true;
            _ink.Strokes.Remove(stroke);
            _suppress = false;
        }
        else
        {
            PushUndo(new StrokeChange(_ink.Strokes, new StrokeCollection { stroke }, new StrokeCollection()));
            SelectStrokes(new StrokeCollection { stroke }, auto: true); // unos segundos, para cambiarle color o grosor
            AdvanceColor();
        }
        e.Handled = true;
    }

    StylusPointCollection BuildPoints(Point a, Point b)
    {
        var pts = new List<Point>();
        switch (_tool)
        {
            case Tool.Rectangle:
                pts.AddRange(new[] { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a });
                break;
            case Tool.Arrow:
                pts.Add(a);
                AddArrowHead(pts, a, b);
                break;
            default: // línea
                pts.Add(a);
                pts.Add(b);
                break;
        }
        return new StylusPointCollection(pts);
    }

    double ArrowHeadLength => Math.Max(18, _size * 5);

    /// <summary>
    /// Agrega la punta en <paramref name="tip"/>, apuntando desde <paramref name="from"/>: un
    /// triángulo relleno. Como es un solo trazo, el relleno es un abanico de líneas desde la
    /// punta a la base, más juntas que el grosor, así no quedan huecos.
    /// </summary>
    void AddArrowHead(List<Point> pts, Point from, Point tip)
    {
        var v = tip - from;
        double len = v.Length;
        pts.Add(tip);
        if (len < 1) return;
        v /= len;
        double head = ArrowHeadLength;
        var n = new Vector(-v.Y, v.X);
        var w1 = tip - v * head + n * head * 0.55;
        var w2 = tip - v * head - n * head * 0.55;
        pts.AddRange(new[] { w1, w2, tip }); // contorno
        int steps = (int)Math.Ceiling((w2 - w1).Length / Math.Max(1, _size * 0.8));
        for (int i = 1; i < steps; i++)
        {
            pts.Add(w1 + (w2 - w1) * i / steps);
            pts.Add(tip);
        }
    }

    /// <summary>
    /// Flecha a mano alzada: el recorrido suavizado y la punta orientada según el
    /// último tramo (una cabeza de largo hacia atrás, para que el temblor final no la tuerza).
    /// </summary>
    StylusPointCollection FreeArrow(List<Point> path)
    {
        var smooth = Smooth(path);
        var tip = smooth[^1];
        var from = smooth[0];
        double walked = 0;
        for (int i = smooth.Count - 2; i >= 0; i--)
        {
            walked += (smooth[i + 1] - smooth[i]).Length;
            from = smooth[i];
            if (walked >= ArrowHeadLength) break;
        }
        var pts = smooth.Take(smooth.Count - 1).ToList();
        AddArrowHead(pts, from, tip);
        return new StylusPointCollection(pts);
    }

    /// <summary>Media móvil de 5 puntos; los extremos no se mueven.</summary>
    static List<Point> Smooth(List<Point> path)
    {
        if (path.Count < 5) return path.ToList();
        var result = new List<Point> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            int from = Math.Max(0, i - 2), to = Math.Min(path.Count - 1, i + 2);
            double x = 0, y = 0;
            for (int j = from; j <= to; j++) { x += path[j].X; y += path[j].Y; }
            result.Add(new Point(x / (to - from + 1), y / (to - from + 1)));
        }
        result.Add(path[^1]);
        return result;
    }

    static double PathLength(List<Point> path)
    {
        double total = 0;
        for (int i = 1; i < path.Count; i++) total += (path[i] - path[i - 1]).Length;
        return total;
    }

    /// <summary>
    /// El recorrido se aparta poco de la recta entre sus extremos (hasta 6 px en pantalla
    /// o el 6 % de su largo, lo que sea mayor): se toma como flecha recta.
    /// </summary>
    bool IsNearlyStraight(List<Point> path)
    {
        var a = path[0];
        var chord = path[^1] - a;
        double len = chord.Length;
        if (len < 1) return false; // vuelve al inicio: es una curva
        double tolerance = Math.Max(6 / _zoom.ScaleX, len * 0.06);
        foreach (var p in path)
        {
            double deviation = Math.Abs(Vector.CrossProduct(chord, p - a)) / len;
            if (deviation > tolerance) return false;
        }
        return true;
    }

    Point Constrain(Point a, Point b)
    {
        var v = b - a;
        if (_tool == Tool.Rectangle)
        {
            double m = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            return new Point(a.X + Math.Sign(v.X) * m, a.Y + Math.Sign(v.Y) * m);
        }
        double step = Math.PI / 4;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
        return a + new Vector(Math.Cos(ang), Math.Sin(ang)) * v.Length;
    }

    /// <param name="existing">Texto ya escrito que se corrige (doble clic); null para uno nuevo.</param>
    void BeginText(Point p, TextBlock existing = null)
    {
        MarkTouched(); // escribiendo: aunque cambie de ventana, no se cierra solo
        _editing = new TextBox
        {
            MinWidth = 80,
            FontSize = existing?.FontSize ?? FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = existing?.Foreground ?? new SolidColorBrush(_penColor),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            Text = existing?.Text ?? "",
        };
        _editing.SetResourceReference(BorderBrushProperty, "Acento");
        Canvas.SetLeft(_editing, p.X);
        Canvas.SetTop(_editing, p.Y);
        _textLayer.Children.Add(_editing);

        _editingBlock = existing;
        if (existing != null)
        {
            existing.Visibility = Visibility.Hidden; // el cuadro de edición ocupa su lugar
            _editing.CaretIndex = _editing.Text.Length; // cursor al final, sin seleccionar: es para corregir
        }

        _editing.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { CommitText(); e.Handled = true; }
        };
        _editing.LostKeyboardFocus += (s, e) => CommitText();
        Dispatcher.InvokeAsync(() => { _editing?.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Doble clic sobre un texto: se vuelve a abrir para corregirlo (con su color y tamaño).</summary>
    void EditText(TextBlock block)
    {
        CommitText();
        ClearSelection();
        BeginText(new Point(Canvas.GetLeft(block), Canvas.GetTop(block)), block);
    }

    void CommitText()
    {
        if (_editing == null) return;
        var tb = _editing;
        _editing = null;                 // primero, para que LostKeyboardFocus no reentre
        _textLayer.Children.Remove(tb);

        if (_editingBlock != null)
        {
            // Corrección de un texto existente
            var edited = _editingBlock;
            _editingBlock = null;
            edited.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                _textLayer.Children.Remove(edited); // quedó vacío: se borra
                PushUndo(new ElementRemove(_textLayer, edited));
                return;
            }
            var before = (edited.Text, edited.Foreground, edited.FontSize);
            var after = (tb.Text, tb.Foreground, tb.FontSize);
            if (before == after) return;
            edited.Text = after.Text;
            edited.Foreground = after.Foreground;
            edited.FontSize = after.FontSize;
            PushUndo(new TextEdit(edited, before, after));
            return;
        }

        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var block = AddTextBlock(new TextNote
        {
            Text = tb.Text,
            X = Canvas.GetLeft(tb),
            Y = Canvas.GetTop(tb),
            FontSize = tb.FontSize,
            Color = ((SolidColorBrush)tb.Foreground).Color.ToString(),
            Fondo = AppSettings.Current.TextoConFondo, // botón "texto con fondo" de la barra
        });
        PushUndo(new ElementAdd(_textLayer, block));
    }

    TextBlock AddTextBlock(TextNote n)
    {
        var block = new NoteText(n.Fondo)
        {
            Text = n.Text,
            FontSize = n.FontSize > 0 ? n.FontSize : FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ParseColor(n.Color)),
        };
        Canvas.SetLeft(block, n.X);
        Canvas.SetTop(block, n.Y);
        if (n.Orden > 0) block.Tag = n.Orden; // orden para la numeración (si no, se asigna al refrescar)
        // Doble clic para corregirlo. Con Seleccionar, un clic lo elige y arrastrar lo mueve;
        // con las demás herramientas un clic simple no hace nada (no empieza un trazo encima).
        block.Cursor = Cursors.IBeam;
        block.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2) EditText(block);
            else if (_tool == Tool.Select) PickAndDrag(block, e.GetPosition(_ink));
            e.Handled = true;
        };
        _textLayer.Children.Add(block);
        return block;
    }

    static Color ParseColor(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return Palette[0].color; }
    }
}
