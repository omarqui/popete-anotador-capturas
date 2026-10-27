// ----------------------------------------------------------------------------
//  Editor — la tarjeta de la idea
//
//  El campo de la idea (_ideaBox, EditorView.Ideas.cs) va dentro de una tarjeta
//  que flota sobre el lienzo, fuera del zoom: mide siempre lo mismo en pantalla.
//  Aparece cerca de su número sin tapar lo marcado (IdeaCardPlacement), nunca
//  se sale de la vista; su cabecera dice de qué número es. Se arrastra desde la
//  cabecera; lo movido solo dura mientras está abierta. Cabecera con el número
//  y × para quitar la idea; texto guía y atajos al pie; crece hasta ~8 líneas.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    const double IdeaCardWidth = 320;
    const double IdeaFontSize = 15;

    Canvas _ideaOverlay;                    // sobre el lienzo, al lado del puntero propio
    Border _ideaCard;                       // la tarjeta abierta, o null
    Vector _ideaCardShift;                  // lo que el usuario la arrastró desde su lugar
    int _ideaCardSide = -1;                 // IdeaCardPlacement: el lado elegido al abrirla
    Point? _ideaDragFrom;
    Vector _ideaShiftAtDrag;

    Canvas BuildIdeaOverlay()
    {
        _ideaOverlay = new Canvas { ClipToBounds = true }; // sin fondo: solo la tarjeta recibe clics
        _ideaOverlay.SizeChanged += (s, e) => PlaceIdeaCard();
        _scroll.ScrollChanged += (s, e) => PlaceIdeaCard();
        return _ideaOverlay;
    }

    /// <summary>El número que muestra (o mostraría, si todavía no tiene idea) <paramref name="target"/>.</summary>
    int NumberShownFor(object target)
    {
        var (targets, candidates) = NumberCandidates();
        int i = targets.IndexOf(target);
        if (i < 0) return 0;
        int n = Ideas.Assign(candidates, NumbersOn)[i];
        return n > 0 ? n : Ideas.GhostNumber(candidates, i);
    }

    /// <summary>Pone <paramref name="box"/> en una tarjeta nueva para <paramref name="target"/> y la muestra.</summary>
    void ShowIdeaCard(TextBox box, object target)
    {
        // Cabecera: número, título y quitar; se arrastra desde aquí
        var color = NumberColorOf(target);
        var chip = NumberBadge(NumberShownFor(target), color, 10);
        chip.VerticalAlignment = VerticalAlignment.Center;
        var title = new TextBlock { Text = "Idea", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Texto");
        var remove = Ui.IconBtn(Icons.Close, "Quitar la idea (Ctrl+Z la devuelve)", RemoveIdea);
        remove.Width = remove.Height = 26;
        remove.Focusable = false; // no le quita el foco al campo
        var header = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Margin = new Thickness(10, 6, 4, 0) };
        header.ToolTip = "Arrastra para moverla";
        DockPanel.SetDock(chip, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(chip);
        header.Children.Add(remove);
        header.Children.Add(title);
        HookIdeaCardDrag(header);

        // El campo, con su texto guía mientras está vacío. Crece hasta ~8 líneas y después se desplaza.
        box.FontSize = IdeaFontSize;
        box.BorderThickness = new Thickness(0);
        box.Background = Brushes.Transparent;
        box.Padding = new Thickness(8, 4, 8, 4);
        box.MaxHeight = IdeaFontSize * 1.4 * 8 + 8;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Texto");
        var hint = new TextBlock
        {
            Text = "Qué quieres decir con este número…",
            FontSize = IdeaFontSize,
            Margin = new Thickness(12, 4, 12, 4),
            IsHitTestVisible = false,
            Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        box.TextChanged += (s, e) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var editor = new Grid { Margin = new Thickness(4, 2, 4, 0) };
        editor.Children.Add(box);
        editor.Children.Add(hint);

        var footer = new TextBlock
        {
            Text = "Enter guarda · Shift+Enter, otra línea · Esc cierra",
            FontSize = 11.5,
            Margin = new Thickness(12, 2, 12, 8),
        };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");

        var panel = new StackPanel();
        panel.Children.Add(header);
        panel.Children.Add(editor);
        panel.Children.Add(footer);
        _ideaCard = new Border
        {
            Width = IdeaCardWidth,
            Child = panel,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.3 },
        };
        _ideaCard.SetResourceReference(Border.BackgroundProperty, "Superficie");
        _ideaCard.SetResourceReference(Border.BorderBrushProperty, "Borde");
        _ideaCard.SizeChanged += (s, e) => PlaceIdeaCard(rechoose: false); // crece con el texto, sin saltar de lado

        _ideaCardShift = new Vector();
        _ideaCardSide = -1;
        _ideaOverlay.Children.Add(_ideaCard);
        PlaceIdeaCard();
    }

    void HideIdeaCard()
    {
        if (_ideaCard == null) return;
        _ideaOverlay.Children.Remove(_ideaCard);
        _ideaCard = null;
        _ideaDragFrom = null;
    }

    /// <summary>La × de la cabecera: quita la idea (con deshacer) y cierra.</summary>
    void RemoveIdea()
    {
        if (_ideaBox == null) return;
        _ideaBox.Text = "";
        CommitIdea();
    }

    /// <summary>
    /// Al lado de lo marcado sin taparlo (IdeaCardPlacement), más lo que la arrastró el usuario,
    /// siempre dentro de la vista. Corre al abrirla, al crecer y al desplazar, hacer zoom o arrastrar.
    /// </summary>
    /// <param name="rechoose">
    /// Vuelve a elegir el lado (al abrir, desplazar o hacer zoom). Al crecer con el texto o al
    /// arrastrarla no: la tarjeta no salta de lado mientras se escribe.
    /// </param>
    void PlaceIdeaCard(bool rechoose = true)
    {
        if (_ideaCard == null || _ideaTarget == null || !IsAlive(_ideaTarget)) return;
        const double margin = IdeaCardPlacement.Margin;
        double r = NumberRadius * _zoom.ScaleX;
        var anchor = _board.TranslatePoint(AutoNumberCenter(_ideaTarget), _ideaOverlay);
        _ideaCard.Measure(new Size(IdeaCardWidth, double.PositiveInfinity));
        double w = IdeaCardWidth, h = _ideaCard.DesiredSize.Height;
        var view = new Rect(0, 0, _ideaOverlay.ActualWidth, _ideaOverlay.ActualHeight);
        var size = new Size(w, h);
        var marked = MarkedOnScreen(_ideaTarget, anchor, r);

        if (_ideaCardSide < 0 || (rechoose && _ideaCardShift.Length == 0))
            _ideaCardSide = IdeaCardPlacement.Choose(marked, anchor, size, view, OtherMarksOnScreen(_ideaTarget));
        var p = IdeaCardPlacement.Position(_ideaCardSide, marked, anchor, size, view);
        double x = Math.Clamp(p.X + _ideaCardShift.X, margin, Math.Max(margin, view.Width - w - margin));
        double y = Math.Clamp(p.Y + _ideaCardShift.Y, margin, Math.Max(margin, view.Height - h - margin));
        Canvas.SetLeft(_ideaCard, x);
        Canvas.SetTop(_ideaCard, y);
    }

    /// <summary>Lo marcado (el trazo o el texto, con su número) en coordenadas de la vista.</summary>
    Rect MarkedOnScreen(object target, Point number, double r)
    {
        var marked = BoundsOnScreen(target);
        marked.Union(new Rect(number.X - r, number.Y - r, 2 * r, 2 * r));
        return marked;
    }

    /// <summary>Los demás trazos y textos, para que la tarjeta tampoco los tape si puede.</summary>
    List<Rect> OtherMarksOnScreen(object target) =>
        _ink.Strokes.Where(s => s != target).Cast<object>()
            .Concat(TextBlocks.Where(tb => tb != target))
            .Select(BoundsOnScreen).ToList();

    Rect BoundsOnScreen(object target)
    {
        var bounds = target is Stroke s
            ? s.GetBounds()
            : new Rect(Canvas.GetLeft((TextBlock)target), Canvas.GetTop((TextBlock)target), ((TextBlock)target).ActualWidth, ((TextBlock)target).ActualHeight);
        return new Rect(_board.TranslatePoint(bounds.TopLeft, _ideaOverlay), _board.TranslatePoint(bounds.BottomRight, _ideaOverlay));
    }

    void HookIdeaCardDrag(FrameworkElement header)
    {
        header.MouseLeftButtonDown += (s, e) =>
        {
            _ideaDragFrom = e.GetPosition(_ideaOverlay);
            _ideaShiftAtDrag = _ideaCardShift;
            header.CaptureMouse(); // el foco se queda en el campo
            e.Handled = true;
        };
        header.MouseMove += (s, e) =>
        {
            if (_ideaDragFrom is not Point from) return;
            _ideaCardShift = _ideaShiftAtDrag + (e.GetPosition(_ideaOverlay) - from);
            PlaceIdeaCard(rechoose: false);
        };
        header.MouseLeftButtonUp += (s, e) =>
        {
            _ideaDragFrom = null;
            header.ReleaseMouseCapture();
            e.Handled = true;
        };
        header.LostMouseCapture += (s, e) => _ideaDragFrom = null;
    }
}
