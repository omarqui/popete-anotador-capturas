// ----------------------------------------------------------------------------
//  Editor — la tarjeta de la idea
//
//  El campo de la idea (_ideaBox, EditorView.Ideas.cs) va dentro de una tarjeta
//  que flota sobre el lienzo, fuera del zoom: mide siempre lo mismo en pantalla.
//  Aparece junto a su número (a la derecha, o a la izquierda si no entra), nunca
//  se sale de la vista y una línea fina la une al número. Se arrastra desde la
//  cabecera; lo movido solo dura mientras está abierta. Cabecera con el número
//  y × para quitar la idea; texto guía y atajos al pie; crece hasta ~8 líneas.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    const double IdeaCardWidth = 320;
    const double IdeaFontSize = 15;

    Canvas _ideaOverlay;                    // sobre el lienzo, al lado del puntero propio
    Border _ideaCard;                       // la tarjeta abierta, o null
    System.Windows.Shapes.Line _ideaLink;   // del número a la tarjeta
    Vector _ideaCardShift;                  // lo que el usuario la arrastró desde su lugar
    Point? _ideaDragFrom;
    Vector _ideaShiftAtDrag;

    Canvas BuildIdeaOverlay()
    {
        _ideaOverlay = new Canvas { ClipToBounds = true }; // sin fondo: solo la tarjeta recibe clics
        _ideaLink = new System.Windows.Shapes.Line { StrokeThickness = 1.5, Opacity = 0.8, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _ideaOverlay.Children.Add(_ideaLink);
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
        _ideaCard.SizeChanged += (s, e) => PlaceIdeaCard(); // crece con el texto

        _ideaLink.Stroke = new SolidColorBrush(color);
        _ideaCardShift = new Vector();
        _ideaOverlay.Children.Add(_ideaCard);
        PlaceIdeaCard();
    }

    void HideIdeaCard()
    {
        if (_ideaCard == null) return;
        _ideaOverlay.Children.Remove(_ideaCard);
        _ideaCard = null;
        _ideaDragFrom = null;
        _ideaLink.Visibility = Visibility.Collapsed;
    }

    /// <summary>La × de la cabecera: quita la idea (con deshacer) y cierra.</summary>
    void RemoveIdea()
    {
        if (_ideaBox == null) return;
        _ideaBox.Text = "";
        CommitIdea();
    }

    /// <summary>
    /// Junto al número (a la derecha; a la izquierda si no entra), más lo que la arrastró el usuario,
    /// siempre dentro de la vista. Corre al abrirla, al crecer y al desplazar, hacer zoom o arrastrar.
    /// </summary>
    void PlaceIdeaCard()
    {
        if (_ideaCard == null || _ideaTarget == null || !IsAlive(_ideaTarget)) return;
        const double gap = 14, margin = 8;
        double r = NumberRadius * _zoom.ScaleX;
        var anchor = _board.TranslatePoint(AutoNumberCenter(_ideaTarget), _ideaOverlay);
        _ideaCard.Measure(new Size(IdeaCardWidth, double.PositiveInfinity));
        double w = IdeaCardWidth, h = _ideaCard.DesiredSize.Height;
        double viewW = _ideaOverlay.ActualWidth, viewH = _ideaOverlay.ActualHeight;

        double x = anchor.X + r + gap;
        if (x + w > viewW - margin && anchor.X - r - gap - w >= margin) x = anchor.X - r - gap - w;
        double y = anchor.Y - 18;
        x = Math.Clamp(x + _ideaCardShift.X, margin, Math.Max(margin, viewW - w - margin));
        y = Math.Clamp(y + _ideaCardShift.Y, margin, Math.Max(margin, viewH - h - margin));
        Canvas.SetLeft(_ideaCard, x);
        Canvas.SetTop(_ideaCard, y);

        // La línea va del borde del número al punto más cercano de la tarjeta
        var near = new Point(Math.Clamp(anchor.X, x, x + w), Math.Clamp(anchor.Y, y, y + h));
        var v = near - anchor;
        if (v.Length <= r + 2)
        {
            _ideaLink.Visibility = Visibility.Collapsed; // la tarjeta está encima del número
            return;
        }
        var from = anchor + v / v.Length * r;
        _ideaLink.X1 = from.X;
        _ideaLink.Y1 = from.Y;
        _ideaLink.X2 = near.X;
        _ideaLink.Y2 = near.Y;
        _ideaLink.Visibility = Visibility.Visible;
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
            PlaceIdeaCard();
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
