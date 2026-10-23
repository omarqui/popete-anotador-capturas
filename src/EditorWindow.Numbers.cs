// ----------------------------------------------------------------------------
//  Editor — numeración automática
//
//  Cada trazo (lápiz, resaltador, formas; no las flechas) y cada texto lleva un círculo con su
//  número de orden pegado a su punto más arriba a la izquierda. Los números se recalculan en cada
//  cambio (MarkChanged): si se borra el 2, el 3 pasa a ser el 2.
//
//  El orden de creación se guarda en el propio trazo (propiedad del ISF) y en
//  TextNote.Orden, así sobrevive a cerrar y retomar la captura. Los círculos
//  están dentro de _surface: salen en la imagen exportada.
//  Se activan por captura con el botón "123" (o N); el valor inicial viene de
//  AppSettings.NumerarPorDefecto.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    static readonly Guid OrderProperty = new Guid("5b0e3c1e-8f6a-4d8e-9c1f-2a7d1f0b6c11");

    /// <summary>Marca de flecha (propiedad del ISF): las flechas no llevan número.</summary>
    static readonly Guid ArrowProperty = new Guid("c3a1f6d2-7e4b-4b8a-a5d9-0f2e8b6c4a17");

    static bool IsArrow(Stroke s) => s.ContainsPropertyData(ArrowProperty);

    /// <summary>Desplazamiento del número que puso el usuario (propiedad del ISF: [dx, dy]).</summary>
    static readonly Guid NumberOffsetProperty = new Guid("8d2f4a6b-1c3e-4f5a-9b7d-6e0a2c4f8b13");

    static Vector NumberOffsetOf(object target) => target switch
    {
        Stroke s when s.ContainsPropertyData(NumberOffsetProperty)
            && s.GetPropertyData(NumberOffsetProperty) is double[] { Length: 2 } d => new Vector(d[0], d[1]),
        NoteText nt => nt.NumberOffset,
        _ => new Vector(),
    };

    static void SetNumberOffset(object target, Vector v)
    {
        switch (target)
        {
            case Stroke s when v.Length == 0: s.RemovePropertyData(NumberOffsetProperty); break;
            case Stroke s: s.AddPropertyData(NumberOffsetProperty, new[] { v.X, v.Y }); break;
            case NoteText nt: nt.NumberOffset = v; break;
        }
    }

    // Arrastre de un número: (círculo, su trazo o texto, dónde se agarró, desplazamiento al empezar)
    (FrameworkElement badge, object target, Point start, Vector from)? _numberDrag;
    bool _numberMoved;

    Canvas _numbersLayer;
    ToggleButton _numbersButton;
    long _nextOrder = 1;

    bool NumbersOn => _session.Meta.Numerar ?? AppSettings.Current.NumerarPorDefecto;

    Canvas BuildNumbersLayer()
    {
        // Sin fondo: solo los círculos reciben clics (seleccionan su trazo); el resto pasa a la tinta
        _numbersLayer = new Canvas();
        // Si cambia el tamaño de los números en Ajustes, se redibujan al momento
        Action onSettings = RefreshNumbers;
        AppSettings.Changed += onSettings;
        Closed += (s, e) => AppSettings.Changed -= onSettings;
        return _numbersLayer;
    }

    static long OrderOf(Stroke s) =>
        s.ContainsPropertyData(OrderProperty) ? Convert.ToInt64(s.GetPropertyData(OrderProperty)) : 0;

    IEnumerable<TextBlock> TextBlocks => _textLayer.Children.OfType<TextBlock>();

    /// <summary>Después de cargar: el próximo número de orden sigue al más alto guardado.</summary>
    void InitOrders()
    {
        long max = 0;
        foreach (var s in _ink.Strokes) max = Math.Max(max, OrderOf(s));
        foreach (var tb in TextBlocks) if (tb.Tag is long o) max = Math.Max(max, o);
        _nextOrder = max + 1;
        AssignMissingOrders(); // sesiones viejas: en el orden en que estaban
    }

    void AssignMissingOrders()
    {
        foreach (var s in _ink.Strokes)
            if (s != _preview && !s.ContainsPropertyData(OrderProperty)) s.AddPropertyData(OrderProperty, _nextOrder++);
        foreach (var tb in TextBlocks)
            if (tb.Tag is not long) tb.Tag = _nextOrder++;
    }

    void ToggleNumbers()
    {
        _session.Meta.Numerar = !NumbersOn;
        _numbersButton.IsChecked = NumbersOn;
        MarkChanged();
    }

    void RefreshNumbers()
    {
        if (_numbersLayer == null) return;
        _numbersLayer.Children.Clear();
        if (_numbersButton != null) _numbersButton.IsChecked = NumbersOn;
        if (!NumbersOn) return;
        AssignMissingOrders();

        // (orden, punto del trazo más arriba a la izquierda, medio grosor, color).
        // El número va pegado a ese punto, hacia arriba a la izquierda: así queda
        // en esa zona del trazo pero siempre junto a él (en un círculo, sobre el
        // arco superior izquierdo, no en la esquina vacía de su recuadro).
        var items = new List<(long order, Point at, double halfWidth, Color color, object target)>();
        foreach (var s in _ink.Strokes)
        {
            if (s == _preview || s.StylusPoints.Count == 0 || IsArrow(s)) continue;
            var corner = s.StylusPoints.Select(p => p.ToPoint()).MinBy(p => p.X + p.Y);
            var da = s.DrawingAttributes;
            items.Add((OrderOf(s), corner, Math.Max(da.Width, da.Height) / 2, da.Color, s));
        }
        foreach (var tb in TextBlocks)
        {
            var color = (tb.Foreground as SolidColorBrush)?.Color ?? Palette[0].color;
            items.Add((tb.Tag is long o ? o : long.MaxValue, new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb)), 0, color, tb));
        }

        double r = NumberRadius;
        var upLeft = new Vector(-1, -1);
        upLeft.Normalize();
        int n = 1;
        foreach (var item in items.OrderBy(i => i.order))
        {
            // Borde del círculo a 2 px del borde del trazo, más lo que lo haya movido el usuario
            var center = item.at + upLeft * (r + item.halfWidth + 2) + NumberOffsetOf(item.target);
            var badge = NumberBadge(n++, item.color, r);
            var target = item.target;
            badge.Cursor = Cursors.SizeAll;
            badge.ToolTip = "Arrastra para mover el número · clic para seleccionar · doble clic: volver a su lugar";
            badge.MouseLeftButtonDown += (s, e) => NumberDown(badge, target, e);
            badge.MouseMove += (s, e) => NumberMove(e);
            badge.MouseLeftButtonUp += (s, e) => NumberUp(e);
            badge.LostMouseCapture += (s, e) => _numberDrag = null;            Canvas.SetLeft(badge, center.X - r);
            Canvas.SetTop(badge, center.Y - r);
            _numbersLayer.Children.Add(badge);
        }
    }

    // ------------------------------------------------------------------ mover un número
    // Arrastrar el círculo mueve solo el número (su trazo o texto queda donde está); un clic
    // sin arrastrar selecciona su trazo o texto, como antes. Doble clic: vuelve a su lugar.
    void NumberDown(FrameworkElement badge, object target, MouseButtonEventArgs e)
    {
        e.Handled = true; // tampoco llega al doble clic del lienzo (elegir un trazo)
        CommitText();
        if (target is Stroke st && !_ink.Strokes.Contains(st)) return;
        if (e.ClickCount == 2)
        {
            MoveNumber(target, NumberOffsetOf(target), new Vector());
            return;
        }
        _numberDrag = (badge, target, e.GetPosition(_numbersLayer), NumberOffsetOf(target));
        _numberMoved = false;
        badge.CaptureMouse();
    }

    void NumberMove(MouseEventArgs e)
    {
        if (_numberDrag is not { } d) return;
        var delta = e.GetPosition(_numbersLayer) - d.start;
        if (!_numberMoved && delta.Length * _zoom.ScaleX < 3) return; // todavía es un clic
        _numberMoved = true;
        // Mientras se arrastra solo se corre el círculo; al soltar se guarda y se redibuja
        d.badge.RenderTransform = new TranslateTransform(delta.X, delta.Y);
        e.Handled = true;
    }

    void NumberUp(MouseButtonEventArgs e)
    {
        if (_numberDrag is not { } d) return;
        _numberDrag = null; // antes de soltar el mouse: LostMouseCapture también lo limpia
        d.badge.ReleaseMouseCapture();
        e.Handled = true;
        if (_numberMoved) MoveNumber(d.target, d.from, d.from + (e.GetPosition(_numbersLayer) - d.start));
        else PickAndDrag(d.target, e.GetPosition(_ink)); // fue un clic: selecciona su trazo o texto
        DragEnd(); // el clic no arrastra nada: suelta lo que PickAndDrag dejó empezado
    }

    /// <summary>Cambia el desplazamiento del número (con deshacer) y lo redibuja.</summary>
    void MoveNumber(object target, Vector before, Vector after)
    {
        if (before == after) return;
        SetNumberOffset(target, after);
        PushUndo(new ActionChange(() => SetNumberOffset(target, before), () => SetNumberOffset(target, after)));
    }

    /// <summary>
    /// Radio del círculo en píxeles de la captura: crece un poco en capturas grandes
    /// y se multiplica por el tamaño elegido en Ajustes.
    /// </summary>
    double NumberRadius =>
        Math.Clamp(Math.Max(_image.PixelWidth, _image.PixelHeight) / 110.0, 11, 22) * AppSettings.Current.TamanoNumeros;

    static FrameworkElement NumberBadge(int number, Color color, double r)
    {
        // Número blanco sobre colores oscuros, oscuro sobre claros (amarillo, blanco)
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        var grid = new Grid { Width = 2 * r, Height = 2 * r };
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = Math.Max(1.5, r / 7),
        });
        grid.Children.Add(new TextBlock
        {
            Text = number.ToString(),
            FontSize = r * 1.1,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = luminance > 0.6 ? new SolidColorBrush(Color.FromRgb(27, 27, 31)) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, r * 0.08),
        });
        return grid;
    }
}
