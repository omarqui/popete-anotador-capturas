// ----------------------------------------------------------------------------
//  Editor — numeración automática
//
//  Cada trazo (lápiz, resaltador, formas y flechas libres; no las flechas que salen de otro
//  trazo) y cada texto que tiene idea lleva un círculo con su número pegado a su punto más
//  arriba a la izquierda; sin idea, no lleva número, salvo con el ajuste NumerarSiempre, que
//  numera todo. Van seguidos (1, 2, 3) por orden de creación (Ideas.Assign) y se recalculan
//  en cada cambio (MarkChanged) y al cambiar los ajustes: si se borra el 2, el 3 pasa a ser el 2.
//
//  El orden de creación se guarda en el propio trazo (propiedad del ISF) y en
//  TextNote.Orden, así sobrevive a cerrar y retomar la captura. Los círculos
//  están dentro de _surface: salen en la imagen exportada.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    static readonly Guid OrderProperty = new Guid("5b0e3c1e-8f6a-4d8e-9c1f-2a7d1f0b6c11");

    /// <summary>Marca de flecha (propiedad del ISF). Sin FreeArrowProperty, no lleva número (IsTiedArrow).</summary>
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
    long _nextOrder = 1;

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

    /// <summary>
    /// Lo que puede llevar número, en el orden de los trazos y textos: todos los trazos (menos las
    /// flechas unidas) y todos los textos, con su orden y si tienen idea.
    /// </summary>
    (List<object> targets, List<NumberCandidate> candidates) NumberCandidates()
    {
        AssignMissingOrders();
        var targets = new List<object>();
        var candidates = new List<NumberCandidate>();
        foreach (var s in _ink.Strokes)
        {
            if (s == _preview || s.StylusPoints.Count == 0 || IsTiedArrow(s)) continue;
            targets.Add(s);
            candidates.Add(new NumberCandidate(OrderOf(s), Ideas.HasText(IdeaOf(s))));
        }
        foreach (var tb in TextBlocks)
        {
            targets.Add(tb);
            candidates.Add(new NumberCandidate(tb.Tag is long o ? o : long.MaxValue, Ideas.HasText(IdeaOf(tb))));
        }
        return (targets, candidates);
    }

    /// <summary>Ideas.Assign según el ajuste: solo lo que tiene idea, o todo.</summary>
    static int[] AssignNumbers(IReadOnlyList<NumberCandidate> candidates) =>
        Ideas.Assign(candidates, AppSettings.Current.NumerarSiempre);

    /// <summary>
    /// Los que llevan número, en orden: los que tienen idea, o todos con NumerarSiempre (seguidos: 1, 2, 3).
    /// </summary>
    List<(object target, int number)> Numbering()
    {
        var (targets, candidates) = NumberCandidates();
        var numbers = AssignNumbers(candidates);
        return Enumerable.Range(0, targets.Count).Where(i => numbers[i] > 0).OrderBy(i => numbers[i])
            .Select(i => (targets[i], numbers[i])).ToList();
    }

    /// <summary>
    /// Centro del círculo del número de un trazo o texto, lo lleve o no. Va pegado al punto del
    /// trazo más arriba a la izquierda, hacia arriba a la izquierda: así queda en esa zona del
    /// trazo pero siempre junto a él (en un círculo, sobre el arco superior izquierdo, no en la
    /// esquina vacía de su recuadro). Los textos, en su esquina. Más lo que lo movió el usuario.
    /// </summary>
    Point AutoNumberCenter(object target)
    {
        Point at;
        double halfWidth = 0;
        if (target is Stroke s)
        {
            if (s.StylusPoints.Count == 0) return new Point();
            at = s.StylusPoints.Select(p => p.ToPoint()).MinBy(p => p.X + p.Y);
            halfWidth = Math.Max(s.DrawingAttributes.Width, s.DrawingAttributes.Height) / 2;
        }
        else
        {
            var tb = (TextBlock)target;
            at = new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb));
        }
        var upLeft = new Vector(-1, -1);
        upLeft.Normalize();
        // Borde del círculo a 2 px del borde del trazo
        return at + upLeft * (NumberRadius + halfWidth + 2) + NumberOffsetOf(target);
    }

    static Color NumberColorOf(object target) => target switch
    {
        Stroke s => s.DrawingAttributes.Color,
        TextBlock tb => (tb.Foreground as SolidColorBrush)?.Color ?? Palette[0].color,
        _ => Palette[0].color,
    };

    void RefreshNumbers()
    {
        if (_numbersLayer == null) return;
        _numbersLayer.Children.Clear();
        _ideaMarks?.Children.Clear();

        double r = NumberRadius;
        foreach (var (target, number) in Numbering())
        {
            var center = AutoNumberCenter(target);
            var badge = NumberBadge(number, NumberColorOf(target), r);
            badge.Cursor = Cursors.SizeAll;
            // Con idea: el tooltip la muestra y una marca (fuera de la imagen) lo distingue
            var idea = IdeaOf(target);
            if (Ideas.HasText(idea)) AddIdeaMark(center, r);
            badge.ToolTip = Ideas.HasText(idea)
                ? idea
                : "Clic para escribir su idea · arrastra para mover el número · doble clic: volver a su lugar";
            badge.MouseLeftButtonDown += (s, e) => NumberDown(badge, target, e);
            badge.MouseMove += (s, e) => NumberMove(e);
            badge.MouseLeftButtonUp += (s, e) => NumberUp(e);
            badge.LostMouseCapture += (s, e) => _numberDrag = null;            Canvas.SetLeft(badge, center.X - r);
            Canvas.SetTop(badge, center.Y - r);
            _numbersLayer.Children.Add(badge);
        }
        RefreshGhost();
        RefreshIdeasButton();
    }

    // ------------------------------------------------------------------ mover un número
    // Arrastrar el círculo mueve solo el número (su trazo o texto queda donde está); un clic
    // sin arrastrar selecciona su trazo o texto, como antes. Doble clic: vuelve a su lugar.
    void NumberDown(FrameworkElement badge, object target, MouseButtonEventArgs e)
    {
        e.Handled = true; // tampoco llega al doble clic del lienzo (elegir un trazo)
        CommitText();
        if (target is Stroke st && !_ink.Strokes.Contains(st)) return;
        if (!_numbersLayer.Children.Contains(badge))
        {
            // Cerrar el campo de otra idea redibujó los números: este círculo ya no está
            if (e.ClickCount == 1) OpenIdea(target);
            return;
        }
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
        if (!_numberMoved) OpenIdea(d.target); // y abre el campo de su idea
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
