// ----------------------------------------------------------------------------
//  Editor — selección y mover
//
//  El trazo o la forma recién dibujados quedan seleccionados unos segundos
//  (ajustable; cada retoque reinicia la cuenta): elegir un color o un grosor
//  (clic o teclas 1–3) se aplica a la selección, y Supr la borra.
//  Con la herramienta Seleccionar (V) se elige un trazo o un texto con un clic,
//  o varios arrastrando un recuadro sobre una zona vacía (Shift suma a lo que ya
//  había; Shift+clic agrega o quita uno). Ctrl+A selecciona todo. Con cualquier
//  herramienta, un clic en el número de un trazo o un doble clic sobre el trazo lo
//  selecciona (el doble clic está en el constructor de EditorView).
//  Arrastrar algo seleccionado mueve toda la selección; las flechas la mueven
//  1 px (Shift: 10 px). Todo se deshace con Ctrl+Z.
//  Esc quita la selección (y con nada seleccionado cierra).
//
//  El recuadro de selección va en _selectionLayer, fuera de _surface: no sale
//  en la imagen exportada.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    StrokeCollection _selection = new StrokeCollection();
    List<TextBlock> _selectedTexts = new List<TextBlock>();
    Canvas _selectionLayer;
    DispatcherTimer _selectionTimer;
    bool _selectionAuto;   // la puso el dibujo (se suelta sola), no la herramienta Seleccionar

    // Arrastre para mover
    Point? _dragLast;
    Vector _dragTotal;

    // Recuadro de selección (Seleccionar sobre una zona vacía)
    Point? _marqueeStart;
    Grid _marquee;

    Canvas BuildSelectionLayer()
    {
        _selectionLayer = new Canvas { IsHitTestVisible = false };
        // Si un trazo seleccionado desaparece (borrador, recorte), sale de la selección
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (!e.Removed.Any(_selection.Contains)) return;
            _selection = new StrokeCollection(_selection.Where(x => !e.Removed.Contains(x)));
            if (!HasSelection) _selectionTimer?.Stop();
            UpdateSelectionVisual();
        };
        return _selectionLayer;
    }

    bool HasSelection => _selection.Count > 0 || _selectedTexts.Count > 0;

    bool IsSelected(object item) => item switch
    {
        Stroke s => _selection.Contains(s),
        TextBlock tb => _selectedTexts.Contains(tb),
        _ => false,
    };

    /// <param name="auto">
    /// true cuando la selección la pone el dibujo recién hecho: se suelta sola a los
    /// <see cref="AppSettings.SegundosSeleccion"/> segundos (0 = no se selecciona).
    /// </param>
    void SelectStrokes(StrokeCollection strokes, bool auto = false) => Select(strokes, null, auto);

    void Select(IEnumerable<Stroke> strokes, IEnumerable<TextBlock> texts, bool auto = false)
    {
        if (auto && AppSettings.Current.SegundosSeleccion <= 0)
        {
            strokes = null;
            texts = null;
        }
        _selection = new StrokeCollection((strokes ?? Enumerable.Empty<Stroke>()).Distinct());
        _selectedTexts = texts?.Distinct().ToList() ?? new List<TextBlock>();
        _selectionAuto = auto && HasSelection;
        UpdateSelectionVisual();
        RestartSelectionTimer();
        if (HasSelection && !auto) ReflectSelectionInToolbar();
    }

    void SelectOnly(object item)
    {
        if (item is Stroke s) Select(new[] { s }, null);
        else if (item is TextBlock tb) Select(null, new[] { tb });
    }

    /// <summary>Shift+clic: agrega el elemento a la selección, o lo quita si ya estaba.</summary>
    void ToggleSelected(object item)
    {
        var strokes = _selection.ToList();
        var texts = _selectedTexts.ToList();
        if (item is Stroke s && !strokes.Remove(s)) strokes.Add(s);
        if (item is TextBlock tb && !texts.Remove(tb)) texts.Add(tb);
        Select(strokes, texts);
    }

    void SelectAll()
    {
        CommitText();
        Select(_ink.Strokes.Where(s => s != _preview), TextBlocks);
    }

    void RestartSelectionTimer()
    {
        if (_selectionTimer == null)
        {
            _selectionTimer = new DispatcherTimer();
            _selectionTimer.Tick += (s, e) => { _selectionTimer.Stop(); ClearSelection(); };
        }
        _selectionTimer.Stop();
        if (!_selectionAuto) return;
        _selectionTimer.Interval = TimeSpan.FromSeconds(AppSettings.Current.SegundosSeleccion);
        _selectionTimer.Start();
    }

    void ClearSelection()
    {
        if (HasSelection) SelectStrokes(null);
    }

    static Rect TextBounds(TextBlock tb) => new Rect(Canvas.GetLeft(tb), Canvas.GetTop(tb), tb.ActualWidth, tb.ActualHeight);

    /// <summary>Un recuadro punteado por elemento seleccionado; grosor constante en pantalla.</summary>
    void UpdateSelectionVisual()
    {
        if (_selectionLayer == null) return;
        _selectionLayer.Children.Clear();

        double z = _zoom.ScaleX;
        var boxes = _selection.Select(s => s.GetBounds()).Concat(_selectedTexts.Select(TextBounds));
        foreach (var bounds in boxes)
        {
            var b = bounds;
            b.Inflate(6 / z, 6 / z);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = b.Width,
                Height = b.Height,
                RadiusX = 4 / z,
                RadiusY = 4 / z,
                StrokeThickness = 1.5 / z,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            };
            rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");
            Canvas.SetLeft(rect, b.X);
            Canvas.SetTop(rect, b.Y);
            _selectionLayer.Children.Add(rect);
        }
        AddHandles(); // asas para reajustar un trazo elegido a mano (EditorView.Handles.cs)
        if (_marquee != null) _selectionLayer.Children.Add(_marquee);
        if (_cropMode) AddCropOverlay();
    }

    // ------------------------------------------------------------------ cambiar color, grosor, borrar
    /// <summary>Cambia los atributos de los trazos seleccionados; devuelve su deshacer (o null).</summary>
    IUndo ChangeStrokes(Action<DrawingAttributes> change)
    {
        if (_selection.Count == 0) return null;
        var items = new List<(Stroke, DrawingAttributes, DrawingAttributes)>();
        foreach (var s in _selection)
        {
            var before = s.DrawingAttributes.Clone();
            var after = s.DrawingAttributes.Clone();
            change(after);
            s.DrawingAttributes = after;
            items.Add((s, before, after));
        }
        return new AttributeChange(items);
    }

    /// <summary>Cambia color o tamaño de los textos seleccionados; devuelve su deshacer (o null).</summary>
    IUndo ChangeTexts(Brush color = null, double? fontSize = null)
    {
        var edits = new List<IUndo>();
        foreach (var tb in _selectedTexts)
        {
            var before = (tb.Text, tb.Foreground, tb.FontSize);
            var after = (tb.Text, color ?? tb.Foreground, fontSize ?? tb.FontSize);
            tb.Foreground = after.Item2;
            tb.FontSize = after.Item3;
            edits.Add(new TextEdit(tb, before, after));
        }
        return Combine(edits);
    }

    static IUndo Combine(IEnumerable<IUndo> parts)
    {
        var list = parts.Where(p => p != null).ToList();
        return list.Count switch { 0 => null, 1 => list[0], _ => new CompositeChange(list) };
    }

    /// <summary>Registra el deshacer de un retoque a la selección y redibuja su recuadro.</summary>
    void CommitSelectionChange(params IUndo[] parts)
    {
        var change = Combine(parts);
        if (change == null) return;
        PushUndo(change);
        RestartSelectionTimer(); // mientras se la retoca, la selección no se suelta
        // El grosor o el tamaño de letra cambian los recuadros (los textos se miden después)
        Dispatcher.InvokeAsync(UpdateSelectionVisual, DispatcherPriority.Loaded);
    }

    void ApplyColorToSelection(Color color) =>
        CommitSelectionChange(ChangeStrokes(da => da.Color = color), ChangeTexts(color: new SolidColorBrush(color)));

    void ApplySizeToSelection(double size) =>
        CommitSelectionChange(
            ChangeStrokes(da =>
            {
                // Mismas proporciones que MakeAttributes
                da.Width = da.IsHighlighter ? Math.Max(4, size * 1.5) : size;
                da.Height = da.IsHighlighter ? Math.Max(14, size * 4) : size;
            }),
            ChangeTexts(fontSize: FontSizeFor(size)));

    void DeleteSelection()
    {
        if (!HasSelection) return;
        var strokes = _selection;
        var texts = _selectedTexts;
        ClearSelection();

        var parts = new List<IUndo>();
        if (strokes.Count > 0)
        {
            _suppress = true;
            _ink.Strokes.Remove(strokes);
            _suppress = false;
            parts.Add(new StrokeChange(_ink.Strokes, new StrokeCollection(), strokes));
        }
        foreach (var tb in texts)
        {
            _textLayer.Children.Remove(tb);
            parts.Add(new ElementRemove(_textLayer, tb));
        }
        PushUndo(Combine(parts)); // todo junto: un Ctrl+Z lo devuelve todo
    }

    /// <summary>Muestra en la barra el color y el grosor de lo elegido, sin modificarlo.</summary>
    void ReflectSelectionInToolbar()
    {
        if (_selection.Count > 0)
        {
            var da = _selection[_selection.Count - 1].DrawingAttributes;
            if (da.IsHighlighter) _highlightColor = da.Color; else _penColor = da.Color;
            _size = da.IsHighlighter ? Math.Round(da.Height / 4) : da.Width;
        }
        else
        {
            var tb = _selectedTexts[_selectedTexts.Count - 1];
            if (tb.Foreground is SolidColorBrush b) _penColor = b.Color;
            _size = Math.Max(1, Math.Round(tb.FontSize / 5));
        }
        RefreshSwatches();
        RefreshSizes();
        UpdateDrawingAttributes(); // se puede elegir sin soltar el lápiz (doble clic): el próximo trazo sale así
        UpdatePointer();
    }

    // ------------------------------------------------------------------ elegir con el mouse
    /// <summary>Herramienta Seleccionar sobre la tinta: el trazo bajo el clic o, si no hay, un recuadro.</summary>
    void SelectToolDown(Point p)
    {
        if (StrokeUnder(p) is Stroke hit) PickAndDrag(hit, p);
        else BeginMarquee(p);
    }

    /// <summary>El trazo de más arriba bajo <paramref name="p"/>, con la tolerancia de Seleccionar (o null).</summary>
    Stroke StrokeUnder(Point p)
    {
        var hits = _ink.Strokes.HitTest(p, Math.Max(6, 10 / _zoom.ScaleX));
        return hits.Count > 0 ? hits[hits.Count - 1] : null;
    }
    /// <summary>
    /// Clic sobre un trazo, un texto o su número. Si ya estaba seleccionado se arrastra
    /// toda la selección; si no, queda solo él. Con Shift se agrega o se quita.
    /// </summary>
    void PickAndDrag(object item, Point p)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ToggleSelected(item);
            if (!IsSelected(item)) return; // se quitó: no hay nada que arrastrar
        }
        else if (!IsSelected(item)) SelectOnly(item);
        else
        {
            _selectionAuto = false; // se la tomó a mano: ya no se suelta sola
            RestartSelectionTimer();
        }
        BeginDrag(p);
    }

    void BeginMarquee(Point p)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) ClearSelection();
        _marqueeStart = p;
        _marquee = new Grid { Opacity = 0 };
        var fill = new System.Windows.Shapes.Rectangle { Opacity = 0.12 };
        fill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Acento");
        var border = new System.Windows.Shapes.Rectangle
        {
            StrokeThickness = 1.25 / _zoom.ScaleX,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };
        border.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");
        _marquee.Children.Add(fill);
        _marquee.Children.Add(border);
        UpdateSelectionVisual();
        _ink.CaptureMouse();
    }

    bool MarqueeMove(Point p)
    {
        if (_marqueeStart == null) return false;
        var r = new Rect(_marqueeStart.Value, p);
        _marquee.Opacity = 1;
        _marquee.Width = r.Width;
        _marquee.Height = r.Height;
        Canvas.SetLeft(_marquee, r.X);
        Canvas.SetTop(_marquee, r.Y);
        return true;
    }

    /// <summary>Suelta el recuadro: selecciona todo lo que toca (con Shift, además de lo que había).</summary>
    bool MarqueeEnd(Point p)
    {
        if (_marqueeStart == null) return false;
        var r = new Rect(_marqueeStart.Value, p);
        _marqueeStart = null;
        _marquee = null;
        _ink.ReleaseMouseCapture();
        if (r.Width * _zoom.ScaleX < 3 && r.Height * _zoom.ScaleX < 3)
        {
            UpdateSelectionVisual(); // un clic en vacío: solo quita la selección
            return true;
        }

        var area = new RectangleGeometry(r);
        var strokes = _ink.Strokes.Where(s => s != _preview
            && area.FillContainsWithDetail(s.GetGeometry()) != IntersectionDetail.Empty);
        var texts = TextBlocks.Where(tb => tb.IsVisible && TextBounds(tb).IntersectsWith(r));
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            strokes = _selection.Concat(strokes);
            texts = _selectedTexts.Concat(texts);
        }
        Select(strokes.ToList(), texts.ToList());
        return true;
    }

    // ------------------------------------------------------------------ mover
    /// <summary>Empieza a arrastrar la selección (con el mouse capturado por la tinta).</summary>
    void BeginDrag(Point p)
    {
        if (!HasSelection) return;
        _dragLast = p;
        _dragTotal = new Vector();
        _ink.CaptureMouse();
    }

    bool DragMove(Point p)
    {
        if (_dragLast == null) return false;
        var delta = p - _dragLast.Value;
        _dragLast = p;
        Translate(delta);
        _dragTotal += delta;
        return true;
    }

    bool DragEnd()
    {
        if (_dragLast == null) return false;
        _dragLast = null;
        _ink.ReleaseMouseCapture();
        if (_dragTotal.Length > 0.5) PushUndo(MoveChange(_dragTotal));
        return true;
    }

    /// <summary>Flechas del teclado: 1 px (Shift: 10 px).</summary>
    bool NudgeSelection(Key key)
    {
        if (!HasSelection) return false;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var d = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => new Vector(),
        };
        if (d.Length == 0) return false;
        Translate(d);
        PushUndo(MoveChange(d));
        RestartSelectionTimer();
        return true;
    }

    /// <summary>Corre lo seleccionado (sin registrar deshacer).</summary>
    void Translate(Vector d)
    {
        Shift(_selection, _selectedTexts, d);
        UpdateSelectionVisual();
        RefreshNumbers();
    }

    static void Shift(StrokeCollection strokes, IEnumerable<TextBlock> texts, Vector d)
    {
        strokes.Transform(new Matrix(1, 0, 0, 1, d.X, d.Y), false);
        foreach (var tb in texts)
        {
            Canvas.SetLeft(tb, Canvas.GetLeft(tb) + d.X);
            Canvas.SetTop(tb, Canvas.GetTop(tb) + d.Y);
        }
    }

    /// <summary>Deshacer/rehacer de un movimiento: vuelve a correr los mismos elementos.</summary>
    IUndo MoveChange(Vector total)
    {
        var strokes = new StrokeCollection(_selection);
        var texts = _selectedTexts.ToList();
        return new ActionChange(() => Shift(strokes, texts, -total), () => Shift(strokes, texts, total));
    }
}
