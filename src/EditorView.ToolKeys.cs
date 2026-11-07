// ----------------------------------------------------------------------------
//  Editor — cambiar de herramienta con el teclado
//
//  Además de la letra de cada herramienta:
//   - Tab: la anterior de trazo. Varios Tab seguidos recorren las tres de
//     trazo más recientes, como Alt+Tab; una tira junto al puntero muestra adónde va.
//   - 1–9 y 0: por su lugar en la barra, en el orden en que se armó, y después
//     las de «Otras herramientas». El grosor pasó a − y +.
//   - Espacio: paleta radial alrededor del puntero. Mantenerlo, apuntar con el
//     ratón y soltar elige; un toque corto la deja abierta para elegir con clic.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    // ------------------------------------------------------------------ recientes y Tab
    /// <summary>
    /// Herramientas de trazo usadas, la más reciente primero (hasta <see cref="MaxRecentTools"/>).
    /// Es de todos los editores: la herramienta pasa de pestaña en pestaña.
    /// </summary>
    static readonly List<Tool> RecentTools = new List<Tool>();

    /// <summary>Tab solo recorre las que dibujan: Seleccionar, Borrador, Texto y Recortar no entran.</summary>
    static readonly Tool[] StrokeTools = { Tool.Pen, Tool.Highlighter, Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse };
    const int MaxRecentTools = 3;

    static bool IsStrokeTool(Tool t) => Array.IndexOf(StrokeTools, t) >= 0;

    /// <summary>Tab seguidos dentro de este tiempo siguen recorriendo la misma lista.</summary>
    const int TabCycleMs = 900;
    List<Tool> _tabCycle;   // las recientes como estaban al primer Tab
    int _tabCycleIndex;
    DateTime _lastTab;

    /// <summary>Anota la herramienta como la más reciente (lo llama SetTool), si es de trazo; la cuarta se cae.</summary>
    static void TouchRecentTool(Tool t)
    {
        if (!IsStrokeTool(t)) return;
        RecentTools.Remove(t);
        RecentTools.Insert(0, t);
        if (RecentTools.Count > MaxRecentTools) RecentTools.RemoveRange(MaxRecentTools, RecentTools.Count - MaxRecentTools);
    }

    void PreviousTool()
    {
        var now = DateTime.Now;
        bool continuing = _tabCycle != null
            && (now - _lastTab).TotalMilliseconds < TabCycleMs
            && _tabCycle[_tabCycleIndex] == _tool;
        if (!continuing)
        {
            TouchRecentTool(_tool);
            _tabCycle = RecentTools.ToList();
            // Con menos de dos usadas, se completa con lápiz y flecha
            foreach (var t in new[] { Tool.Pen, Tool.Arrow })
                if (_tabCycle.Count < 2 && !_tabCycle.Contains(t)) _tabCycle.Add(t);
            // Desde una que no es de trazo (Seleccionar, Borrador, Texto, Recortar),
            // el primer Tab va a la última de trazo
            _tabCycleIndex = _tabCycle[0] == _tool ? 0 : -1;
        }
        _tabCycleIndex = (_tabCycleIndex + 1) % _tabCycle.Count;
        var chosen = _tabCycle[_tabCycleIndex];
        // Como Alt+Tab: la elegida queda primera y la de partida segunda
        RecentTools.Clear();
        RecentTools.Add(chosen);
        RecentTools.AddRange(_tabCycle.Where(t => t != chosen));
        _lastTab = now;
        SetTool(chosen);
        ShowToolStrip(_tabCycle, chosen);
    }

    // ------------------------------------------------------------------ números
    /// <summary>Orden de los números (y de la paleta): las de la barra en su orden, después las de «Otras».</summary>
    static List<Tool> NumberedTools() =>
        BarSlots().Where(s => s != MoreSlot).Select(Enum.Parse<Tool>)
            .Concat(MenuTools().Select(a => a.tool))
            .ToList();

    /// <summary>"1"…"9", "0" o null (más de diez herramientas).</summary>
    static string NumberKeyOf(Tool t)
    {
        int i = NumberedTools().IndexOf(t);
        return i is < 0 or > 9 ? null : i == 9 ? "0" : (i + 1).ToString();
    }

    /// <summary>Letra y número de una herramienta, para tooltips y menús: «A · 3».</summary>
    static string ToolKeysText(Tool t)
    {
        var letter = AllTools.First(a => a.tool == t).key;
        var number = NumberKeyOf(t);
        return number != null ? $"{letter} · {number}" : letter;
    }

    /// <summary>1–9 y 0 (fila de arriba o teclado numérico): la herramienta de ese lugar. false si no es un dígito.</summary>
    bool ToolByNumber(Key key)
    {
        int digit = key is >= Key.D0 and <= Key.D9 ? key - Key.D0
                  : key is >= Key.NumPad0 and <= Key.NumPad9 ? key - Key.NumPad0
                  : -1;
        if (digit < 0) return false;
        var tools = NumberedTools();
        int i = digit == 0 ? 9 : digit - 1;
        if (i < tools.Count) SetTool(tools[i]);
        return true;
    }

    /// <summary>− y +: el grosor anterior o siguiente de los tres (desde uno libre, el más cercano en esa dirección).</summary>
    void StepSize(int direction) =>
        SetSize(direction > 0
            ? SizePresets.FirstOrDefault(s => s > _size, SizePresets[^1])
            : SizePresets.LastOrDefault(s => s < _size, SizePresets[0]));

    // ------------------------------------------------------------------ tira de Tab
    Border _toolStrip;
    DispatcherTimer _toolStripTimer;

    /// <summary>Junto al puntero: las recientes con la elegida marcada y su nombre. Se va sola.</summary>
    void ShowToolStrip(List<Tool> tools, Tool chosen)
    {
        if (_ideaOverlay == null) return;
        HideToolStrip();

        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var t in tools.Take(8))
        {
            var cell = ToolCell(t, 32, 18);
            cell.Margin = new Thickness(2, 0, 2, 0);
            if (t == chosen) MarkCell(cell, hovered: true);
            icons.Children.Add(cell);
        }
        var name = new TextBlock
        {
            Text = AllTools.First(a => a.tool == chosen).name,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Texto");
        var stack = new StackPanel();
        stack.Children.Add(icons);
        stack.Children.Add(name);

        _toolStrip = FloatingCard(stack, 8);
        _toolStrip.IsHitTestVisible = false;
        _ideaOverlay.Children.Add(_toolStrip);
        _toolStrip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _toolStrip.DesiredSize;
        var p = PointerInOverlay(out bool inside);
        // Abajo a la derecha del puntero, sin salirse; con el puntero fuera del lienzo, abajo al centro
        double x = inside ? p.X + 24 : (_ideaOverlay.ActualWidth - size.Width) / 2;
        double y = inside ? p.Y + 24 : _ideaOverlay.ActualHeight - size.Height - 24;
        Canvas.SetLeft(_toolStrip, Math.Clamp(x, 8, Math.Max(8, _ideaOverlay.ActualWidth - size.Width - 8)));
        Canvas.SetTop(_toolStrip, Math.Clamp(y, 8, Math.Max(8, _ideaOverlay.ActualHeight - size.Height - 8)));

        _toolStripTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TabCycleMs) };
        _toolStripTimer.Tick -= ToolStripTick;
        _toolStripTimer.Tick += ToolStripTick;
        _toolStripTimer.Stop();
        _toolStripTimer.Start();
    }

    void ToolStripTick(object sender, EventArgs e)
    {
        _toolStripTimer.Stop();
        HideToolStrip();
    }

    void HideToolStrip()
    {
        if (_toolStrip == null) return;
        _ideaOverlay.Children.Remove(_toolStrip);
        _toolStrip = null;
    }

    // ------------------------------------------------------------------ paleta radial
    const double PaletteRadius = 84;    // del centro a cada herramienta
    const double PaletteCell = 40;
    const double PaletteDeadZone = 24;  // más cerca del centro no se elige nada
    const int PaletteStickyMs = 300;    // un toque más corto la deja abierta

    Border _paletteShield;              // tapa el lienzo mientras está abierta: un clic no dibuja
    Canvas _palette;
    readonly List<(Tool tool, Border cell)> _paletteCells = new List<(Tool, Border)>();
    TextBlock _paletteName, _paletteKeys;
    Point _paletteCenter;
    Tool? _paletteHover;
    DateTime _paletteOpenedAt;
    bool _paletteSticky;                // se soltó Espacio sin elegir: sigue abierta hasta un clic, Esc o Espacio

    bool PaletteOpen => _palette != null;

    void OpenPalette()
    {
        if (PaletteOpen || _ideaOverlay == null || _ideaOverlay.ActualWidth < 1) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed) return; // a mitad de un trazo, no
        CommitText();
        HideToolStrip();

        double outer = PaletteRadius + PaletteCell / 2 + 12;
        double w = _ideaOverlay.ActualWidth, h = _ideaOverlay.ActualHeight;
        var p = PointerInOverlay(out bool inside);
        if (!inside) p = new Point(w / 2, h / 2);
        _paletteCenter = new Point(
            Math.Clamp(p.X, outer, Math.Max(outer, w - outer)),
            Math.Clamp(p.Y, outer, Math.Max(outer, h - outer)));

        // Casi transparente, no del todo: en la captura en el lugar (ventana transparente) un píxel con
        // alfa 0 deja pasar el clic al programa de abajo, y eso la copiaría y cerraría
        _paletteShield = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Width = w, Height = h, Cursor = Cursors.Arrow };
        _paletteShield.MouseMove += (s, e) => PaletteHoverAt(e.GetPosition(_ideaOverlay));
        _paletteShield.MouseLeftButtonDown += (s, e) =>
        {
            PaletteHoverAt(e.GetPosition(_ideaOverlay));
            ClosePalette(apply: true); // fuera de la paleta (sin nada apuntado) solo la cierra
            e.Handled = true;
        };
        // El clic derecho no copia ni cierra el editor mientras está abierta: solo la cierra
        _paletteShield.PreviewMouseRightButtonUp += (s, e) => { ClosePalette(apply: false); e.Handled = true; };

        var disk = new Border
        {
            Width = 2 * outer,
            Height = 2 * outer,
            CornerRadius = new CornerRadius(outer),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.3 },
        };
        disk.SetResourceReference(Border.BackgroundProperty, "Superficie");
        disk.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");

        _palette = new Canvas { Width = 2 * outer, Height = 2 * outer, IsHitTestVisible = false };
        _palette.Children.Add(disk);

        _paletteCells.Clear();
        var tools = NumberedTools();
        for (int i = 0; i < tools.Count; i++)
        {
            double angle = (-90 + i * 360.0 / tools.Count) * Math.PI / 180; // desde arriba, en el sentido del reloj
            var cell = ToolCell(tools[i], PaletteCell, 20);
            if (tools[i] == _tool) MarkCell(cell, hovered: false);
            Canvas.SetLeft(cell, outer + PaletteRadius * Math.Cos(angle) - PaletteCell / 2);
            Canvas.SetTop(cell, outer + PaletteRadius * Math.Sin(angle) - PaletteCell / 2);
            _palette.Children.Add(cell);
            _paletteCells.Add((tools[i], cell));
        }

        // En el centro, el nombre y las teclas de la apuntada (o de la actual)
        _paletteName = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        _paletteName.SetResourceReference(TextBlock.ForegroundProperty, "Texto");
        _paletteKeys = new TextBlock { FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        _paletteKeys.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var center = new StackPanel { Width = 2 * (PaletteRadius - PaletteCell / 2 - 4) };
        center.Children.Add(_paletteName);
        center.Children.Add(_paletteKeys);
        center.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(center, outer - center.Width / 2);
        Canvas.SetTop(center, outer - center.DesiredSize.Height / 2);
        _palette.Children.Add(center);

        Canvas.SetLeft(_palette, _paletteCenter.X - outer);
        Canvas.SetTop(_palette, _paletteCenter.Y - outer);
        _ideaOverlay.Children.Add(_paletteShield);
        _ideaOverlay.Children.Add(_palette);
        if (_pointerLayer != null) _pointerLayer.Visibility = Visibility.Hidden;

        _paletteHover = null;
        _paletteSticky = false;
        _paletteOpenedAt = DateTime.Now;
        PaletteHoverAt(Mouse.GetPosition(_ideaOverlay));

        if (SystemParameters.ClientAreaAnimation)
        {
            var scale = new ScaleTransform(0.85, 0.85, outer, outer);
            _palette.RenderTransform = scale;
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            _palette.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
        }
    }

    /// <summary>Apunta la herramienta en la dirección del puntero (fuera de la zona muerta del centro).</summary>
    void PaletteHoverAt(Point p)
    {
        if (!PaletteOpen) return;
        var v = p - _paletteCenter;
        Tool? hover = null;
        if (v.Length >= PaletteDeadZone && _paletteCells.Count > 0)
        {
            double step = 360.0 / _paletteCells.Count;
            double angle = Math.Atan2(v.Y, v.X) * 180 / Math.PI + 90 + step / 2; // 0 = arriba
            int i = (int)Math.Floor(((angle % 360) + 360) % 360 / step) % _paletteCells.Count;
            hover = _paletteCells[i].tool;
        }
        if (hover == _paletteHover && _paletteName.Text.Length > 0) return;
        _paletteHover = hover;
        foreach (var (tool, cell) in _paletteCells)
        {
            if (tool == hover) MarkCell(cell, hovered: true);
            else if (tool == _tool) MarkCell(cell, hovered: false);
            else UnmarkCell(cell);
        }
        var shown = hover ?? _tool;
        _paletteName.Text = AllTools.First(a => a.tool == shown).name;
        _paletteKeys.Text = ToolKeysText(shown);
    }

    /// <param name="apply">Elige la apuntada (si hay).</param>
    void ClosePalette(bool apply)
    {
        if (!PaletteOpen) return;
        var chosen = apply ? _paletteHover : null;
        _ideaOverlay.Children.Remove(_palette);
        _ideaOverlay.Children.Remove(_paletteShield);
        _palette = null;
        _paletteShield = null;
        _paletteCells.Clear();
        if (_pointerLayer != null) _pointerLayer.Visibility = Visibility.Visible;
        if (chosen is Tool t) SetTool(t);
        UpdatePointer();
    }

    /// <summary>Teclas con la paleta abierta. true si la tecla ya se usó.</summary>
    bool PaletteKeyDown(KeyEventArgs e)
    {
        if (!PaletteOpen) return false;
        switch (e.Key)
        {
            case Key.Space:
                // La repetición de la tecla mantenida no hace nada; un Espacio nuevo con la paleta fija la cierra
                if (!e.IsRepeat && _paletteSticky) ClosePalette(apply: false);
                e.Handled = true;
                return true;
            case Key.Escape:
                ClosePalette(apply: false);
                e.Handled = true;
                return true;
            case Key.Enter:
                ClosePalette(apply: true);
                e.Handled = true;
                return true;
            default:
                ClosePalette(apply: false); // cualquier otra (una letra, un número) sigue su camino
                return false;
        }
    }

    /// <summary>Al soltar Espacio: elige la apuntada; sin nada apuntado, un toque corto la deja abierta y uno largo la cierra.</summary>
    bool PaletteKeyUp(KeyEventArgs e)
    {
        if (!PaletteOpen || e.Key != Key.Space || _paletteSticky) return false;
        if (_paletteHover == null && (DateTime.Now - _paletteOpenedAt).TotalMilliseconds < PaletteStickyMs)
            _paletteSticky = true;
        else
            ClosePalette(apply: true);
        e.Handled = true;
        return true;
    }

    // ------------------------------------------------------------------ piezas
    /// <summary>Dónde está el puntero en la capa de la tarjeta de ideas (y si cae adentro).</summary>
    Point PointerInOverlay(out bool inside)
    {
        var p = Mouse.GetPosition(_ideaOverlay);
        inside = p.X >= 0 && p.Y >= 0 && p.X <= _ideaOverlay.ActualWidth && p.Y <= _ideaOverlay.ActualHeight;
        return p;
    }

    static Border ToolCell(Tool t, double size, double iconSize)
    {
        var cell = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Child = Ui.Icon(AllTools.First(a => a.tool == t).icon, iconSize),
        };
        cell.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Icono");
        return cell;
    }

    /// <param name="hovered">true: la apuntada o la elegida (acento lleno); false: la actual (solo el borde).</param>
    static void MarkCell(Border cell, bool hovered)
    {
        if (hovered)
        {
            cell.SetResourceReference(Border.BackgroundProperty, "Acento");
            cell.SetResourceReference(Border.BorderBrushProperty, "Acento");
            cell.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextoSobreAcento");
        }
        else
        {
            cell.SetResourceReference(Border.BackgroundProperty, "Seleccion");
            cell.SetResourceReference(Border.BorderBrushProperty, "Acento");
            cell.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Icono");
        }
    }

    static void UnmarkCell(Border cell)
    {
        cell.Background = Brushes.Transparent;
        cell.BorderBrush = Brushes.Transparent;
        cell.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Icono");
    }

    static Border FloatingCard(UIElement child, double padding)
    {
        var card = new Border
        {
            Child = child,
            Padding = new Thickness(padding),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.25 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Superficie");
        card.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");
        return card;
    }
}
