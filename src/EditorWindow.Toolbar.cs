// ----------------------------------------------------------------------------
//  Editor — barra de herramientas, barra de estado y selección de herramienta
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    /// <summary>Grosores de la barra (teclas 1 a 4).</summary>
    static readonly double[] SizePresets = { 2, 4, 8, 14 };

    readonly Dictionary<Tool, ToggleButton> _toolButtons = new Dictionary<Tool, ToggleButton>();
    readonly List<(ToggleButton button, Color color)> _swatches = new List<(ToggleButton, Color)>();
    readonly List<(ToggleButton button, double size)> _sizeButtons = new List<(ToggleButton, double)>();
    readonly TextBlock _sizeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, MinWidth = 34, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _saveText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBlock _zoomText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center };
    FrameworkElement _saveIcon;
    System.Windows.Shapes.Ellipse _pairPrimary, _pairSecondary;
    ToggleButton _recentsButton;
    Button _themeButton;

    // ------------------------------------------------------------------ barra de herramientas
    FrameworkElement BuildToolbar()
    {
        // Izquierda: se reparte en varias filas si la ventana es angosta.
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Ui.Group(
            ToolButton(Icons.Select, Tool.Select, "Seleccionar (V) — clic en un trazo para cambiarle color o grosor; Supr lo borra"),
            ToolButton(Icons.Pen, Tool.Pen, "Lápiz (P) — Tab alterna con la flecha"),
            ToolButton(Icons.Highlighter, Tool.Highlighter, "Resaltador (H)"),
            ToolButton(Icons.Line, Tool.Line, "Línea (L) — Shift: ángulos de 45°"),
            ToolButton(Icons.Arrow, Tool.Arrow, "Flecha (A) — Shift: ángulos de 45°; Tab alterna con el lápiz"),
            ToolButton(Icons.Rectangle, Tool.Rectangle, "Rectángulo (R) — Shift: cuadrado"),
            ToolButton(Icons.Text, Tool.Text, "Texto (T) — clic para escribir"),
            ToolButton(Icons.Eraser, Tool.Eraser, "Borrador (E) — borra trazos completos"),
            ToolButton(Icons.Crop, Tool.Crop, "Recortar (C) — arrastra sobre la captura; Ctrl+Z lo deshace")));

        left.Children.Add(Ui.Divider());

        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        colors.Children.Add(BuildColorPair());
        foreach (var (c, name) in Palette)
        {
            var tip = $"{name} — clic: principal · clic derecho: secundario";
            var sw = new ToggleButton { Background = new SolidColorBrush(c), ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(sw, name);
            sw.SetResourceReference(StyleProperty, "EstiloMuestra");
            var color = c;
            sw.Click += (s, e) => SetColor(color);
            sw.MouseRightButtonUp += (s, e) => { SetSecondaryColor(color); e.Handled = true; };
            _swatches.Add((sw, c));
            colors.Children.Add(sw);
        }
        left.Children.Add(colors);

        left.Children.Add(Ui.Divider());

        var sizes = new List<UIElement>();
        string[] names = { "Fino", "Medio", "Grueso", "Muy grueso" };
        for (int i = 0; i < SizePresets.Length; i++)
        {
            double size = SizePresets[i];
            double dot = new[] { 4.0, 7, 10, 14 }[i];
            var dotEl = new System.Windows.Shapes.Ellipse { Width = dot, Height = dot };
            dotEl.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding
            {
                Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
                RelativeSource = System.Windows.Data.RelativeSource.Self,
            });
            var tip = $"{names[i]}: {size} px ({i + 1})";
            var b = new ToggleButton { Content = dotEl, ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.SetResourceReference(StyleProperty, "EstiloSegmento");
            b.Click += (s, e) => SetSize(size);
            _sizeButtons.Add((b, size));
            sizes.Add(b);
        }
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sizeRow.Children.Add(Ui.Group(sizes.ToArray()));
        _sizeText.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        sizeRow.Children.Add(_sizeText);
        left.Children.Add(sizeRow);

        left.Children.Add(Ui.Divider());

        var edit = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        edit.Children.Add(Ui.IconBtn(Icons.Undo, "Deshacer (Ctrl+Z)", Undo));
        edit.Children.Add(Ui.IconBtn(Icons.Redo, "Rehacer (Ctrl+Y)", Redo));
        edit.Children.Add(Ui.IconBtn(Icons.Trash, "Quitar todas las anotaciones", ClearAll));

        _numbersButton = new ToggleButton
        {
            Content = new TextBlock { Text = "123", FontSize = 12, FontWeight = FontWeights.Bold },
            ToolTip = "Numerar trazos y textos (N)",
        };
        System.Windows.Automation.AutomationProperties.SetName(_numbersButton, "Numerar trazos y textos");
        _numbersButton.SetResourceReference(StyleProperty, "EstiloHerramienta");
        _numbersButton.Click += (s, e) => ToggleNumbers();
        edit.Children.Add(_numbersButton);
        left.Children.Add(edit);

        // Derecha: vista y acciones finales.
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        _recentsButton = new ToggleButton { Content = Ui.Icon(Icons.Recents), ToolTip = "Capturas recientes (Ctrl+H)" };
        System.Windows.Automation.AutomationProperties.SetName(_recentsButton, "Capturas recientes");
        _recentsButton.SetResourceReference(StyleProperty, "EstiloHerramienta");
        _recentsButton.Click += (s, e) => ToggleRecents(_recentsButton.IsChecked == true);
        right.Children.Add(_recentsButton);

        _themeButton = Ui.IconBtn(Icons.Theme, "", Theme.Toggle);
        UpdateThemeButton();
        // El ícono muestra el tema al que se pasa: luna en claro, sol en oscuro
        Action onThemeChanged = UpdateThemeButton;
        Theme.Changed += onThemeChanged;
        Closed += (s, e) => Theme.Changed -= onThemeChanged;
        right.Children.Add(_themeButton);

        right.Children.Add(Ui.Divider());

        // Solo ícono para que la barra quepa en una fila; el texto va en el tooltip
        right.Children.Add(Ui.IconBtn(Icons.Save, "Guardar como PNG o JPG (Ctrl+S)", Save));
        var copy = Ui.IconBtn(Icons.Copy, "Copiar la imagen anotada sin cerrar", () => CopyToClipboard());
        copy.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(copy);

        var copyClose = Ui.Btn("Copiar y cerrar", "Copiar y cerrar (Ctrl+C o Ctrl+Enter)",
            () => { if (CopyToClipboard()) Close(); }, Icons.Check, "EstiloPrimario");
        var chip = new Border
        {
            Child = new TextBlock { Text = "Ctrl+C", FontSize = 11, FontWeight = FontWeights.Normal },
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ((StackPanel)copyClose.Content).Children.Add(chip);
        right.Children.Add(copyClose);

        var dock = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);

        var border = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return border;
    }

    ToggleButton ToolButton(string icon, Tool t, string tip)
    {
        var b = new ToggleButton { Content = Ui.Icon(icon), ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.SetResourceReference(StyleProperty, "EstiloHerramienta");
        b.Click += (s, e) => SetTool(t);
        _toolButtons[t] = b;
        return b;
    }

    void UpdateThemeButton()
    {
        _themeButton.Content = Ui.Icon(Theme.IsDark ? Icons.Sun : Icons.Theme);
        var tip = Theme.IsDark ? "Cambiar a tema claro" : "Cambiar a tema oscuro";
        _themeButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(_themeButton, tip);
    }

    // ------------------------------------------------------------------ barra de estado
    FrameworkElement BuildStatusBar()
    {
        _saveIcon = Ui.Icon(Icons.Check, 14, 2.25);
        _saveIcon.Visibility = Visibility.Collapsed;
        _saveIcon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Exito");
        _saveText.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Texto");

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_saveIcon);
        left.Children.Add(_saveText);
        left.Children.Add(_status);

        var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text) in new[] { ("Ctrl+Z", "Deshacer"), ("Shift", "45° y cuadrados"), ("1–4", "Grosor"), ("Alt / clic der.", "Otro color"), ("Supr", "Borrar selección") })
        {
            hints.Children.Add(Ui.Keycap(key));
            var t = new TextBlock { Text = text, Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            hints.Children.Add(t);
        }

        var zoom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(Ui.Btn("−", "Alejar (Ctrl+−)", () => SetZoom(_zoom.ScaleX / 1.15), style: "EstiloPlano"));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(Ui.Btn("+", "Acercar (Ctrl++)", () => SetZoom(_zoom.ScaleX * 1.15), style: "EstiloPlano"));
        zoom.Children.Add(Ui.Btn("Ajustar", "Ajustar a la ventana (Ctrl+0)", FitZoom, style: "EstiloPlano"));
        zoom.Children.Add(Ui.Btn("1:1", "Tamaño real", () => SetZoom(1 / DpiScale()), style: "EstiloPlano"));
        var settings = Ui.IconBtn(Icons.Settings, "Ajustes", () => SettingsWindow.Show(this));
        settings.Width = settings.Height = 28;
        settings.Margin = new Thickness(6, 0, 0, 0);
        zoom.Children.Add(settings);

        _uncropButton = Ui.Btn("Quitar recorte", "Volver a la captura completa (se puede deshacer)", RemoveCrop, Icons.Crop, "EstiloPlano");
        _uncropButton.Margin = new Thickness(0, 0, 8, 0);
        _uncropButton.Visibility = IsCropped ? Visibility.Visible : Visibility.Collapsed;
        zoom.Children.Insert(0, _uncropButton);

        var bar = new DockPanel { Margin = new Thickness(12, 0, 8, 0), Height = 32, LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(zoom, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(zoom);
        bar.Children.Add(hints);

        var border = new Border { Child = bar, BorderThickness = new Thickness(0, 1, 0, 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return border;
    }

    void ShowSaved(string text, bool ok)
    {
        _saveIcon.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        _saveText.Text = text;
    }

    // ------------------------------------------------------------------ herramientas, color y grosor
    void SetTool(Tool t)
    {
        CommitText();
        _tool = t;
        foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;

        // Elegir una herramienta de dibujo es para dibujar algo nuevo: se suelta la selección
        if (t != Tool.Select) ClearSelection();

        _ink.EditingMode = t switch
        {
            Tool.Pen or Tool.Highlighter => InkCanvasEditingMode.Ink,
            Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        // El puntero del sistema se oculta: lo reemplaza el de EditorWindow.Pointer.cs.
        // Seleccionar usa la flecha normal.
        _ink.UseCustomCursor = true;
        _ink.Cursor = t == Tool.Select ? Cursors.Arrow : Cursors.None;

        RefreshSwatches();
        UpdateDrawingAttributes();
        UpdatePointer();
    }

    /// <param name="applyToSelection">Si hay trazos seleccionados, también se pintan (no al intercambiar con Alt).</param>
    void SetColor(Color color, bool applyToSelection = true)
    {
        CurrentColor = color;
        RefreshSwatches();
        UpdateDrawingAttributes();
        if (_editing != null) _editing.Foreground = new SolidColorBrush(color);
        if (applyToSelection) ApplyColorToSelection(color);
        UpdatePointer();
    }

    void SetSecondaryColor(Color color)
    {
        SecondaryColor = color;
        RefreshSwatches();
    }

    /// <summary>Intercambia principal y secundario (tocar Alt o clic en el par de colores).</summary>
    void SwapColors()
    {
        (CurrentColor, SecondaryColor) = (SecondaryColor, CurrentColor);
        SetColor(CurrentColor, applyToSelection: false); // es para el próximo trazo, no para el anterior
    }

    /// <summary>Dos círculos superpuestos: principal adelante, secundario atrás. Clic: intercambiar.</summary>
    FrameworkElement BuildColorPair()
    {
        System.Windows.Shapes.Ellipse Circle(double size, Thickness margin, HorizontalAlignment h, VerticalAlignment v)
        {
            var e = new System.Windows.Shapes.Ellipse { Width = size, Height = size, StrokeThickness = 2, Margin = margin, HorizontalAlignment = h, VerticalAlignment = v };
            e.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Fondo"); // separa los dos círculos
            return e;
        }
        _pairSecondary = Circle(18, new Thickness(0, 0, 2, 2), HorizontalAlignment.Right, VerticalAlignment.Bottom);
        _pairPrimary = Circle(20, new Thickness(2, 2, 0, 0), HorizontalAlignment.Left, VerticalAlignment.Top);

        var grid = new Grid { Width = 32, Height = 32 };
        grid.Children.Add(_pairSecondary);
        grid.Children.Add(_pairPrimary);

        var tip = "Color principal y secundario — Alt, clic derecho en la imagen o clic aquí para intercambiar";
        var b = new Button { Content = grid, ToolTip = tip, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.SetResourceReference(StyleProperty, "EstiloIcono");
        b.Click += (s, e) => SwapColors();
        return b;
    }

    void SetSize(double size)
    {
        _size = Math.Clamp(size, 1, 20);
        RefreshSizes();
        UpdateDrawingAttributes();
        if (_editing != null) _editing.FontSize = FontSizeFor(_size);
        ApplySizeToSelection(_size);
        UpdatePointer();
    }

    void RefreshSwatches()
    {
        var current = CurrentColor;
        foreach (var (b, c) in _swatches) b.IsChecked = c == current;
        _pairPrimary.Fill = new SolidColorBrush(current);
        _pairSecondary.Fill = new SolidColorBrush(SecondaryColor);
    }

    void RefreshSizes()
    {
        foreach (var (b, s) in _sizeButtons) b.IsChecked = s == _size;
        _sizeText.Text = $"{_size} px";
    }

    DrawingAttributes MakeAttributes()
    {
        bool hl = _tool == Tool.Highlighter;
        return new DrawingAttributes
        {
            Color = CurrentColor,
            Width = hl ? Math.Max(4, _size * 1.5) : _size,
            Height = hl ? Math.Max(14, _size * 4) : _size,
            IsHighlighter = hl,
            StylusTip = hl ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = _tool is Tool.Pen or Tool.Highlighter,
            IgnorePressure = true,
        };
    }

    void UpdateDrawingAttributes() => _ink.DefaultDrawingAttributes = MakeAttributes();

    static double FontSizeFor(double size) => Math.Max(14, size * 5);
}
