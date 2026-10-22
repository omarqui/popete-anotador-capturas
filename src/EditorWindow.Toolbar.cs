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
    ToggleButton _recentsButton;

    // ------------------------------------------------------------------ barra de herramientas
    FrameworkElement BuildToolbar()
    {
        // Izquierda: se reparte en varias filas si la ventana es angosta.
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Ui.Group(
            ToolButton(Icons.Pen, Tool.Pen, "Lápiz (P)"),
            ToolButton(Icons.Highlighter, Tool.Highlighter, "Resaltador (H)"),
            ToolButton(Icons.Line, Tool.Line, "Línea (L) — Shift: ángulos de 45°"),
            ToolButton(Icons.Arrow, Tool.Arrow, "Flecha (A) — Shift: ángulos de 45°"),
            ToolButton(Icons.Rectangle, Tool.Rectangle, "Rectángulo (R) — Shift: cuadrado"),
            ToolButton(Icons.Text, Tool.Text, "Texto (T) — clic para escribir"),
            ToolButton(Icons.Eraser, Tool.Eraser, "Borrador (E) — borra trazos completos")));

        left.Children.Add(Ui.Divider());

        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (c, name) in Palette)
        {
            var sw = new ToggleButton { Background = new SolidColorBrush(c), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(sw, name);
            sw.SetResourceReference(StyleProperty, "Muestra");
            var color = c;
            sw.Click += (s, e) => SetColor(color);
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
            b.SetResourceReference(StyleProperty, "Segmento");
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
        edit.Children.Add(Ui.IconBtn(Icons.Trash, "Quitar todas las anotaciones", ClearAll));
        left.Children.Add(edit);

        // Derecha: vista y acciones finales.
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        _recentsButton = new ToggleButton { Content = Ui.Icon(Icons.Recents), ToolTip = "Capturas recientes (Ctrl+H)" };
        System.Windows.Automation.AutomationProperties.SetName(_recentsButton, "Capturas recientes");
        _recentsButton.SetResourceReference(StyleProperty, "Herramienta");
        _recentsButton.Click += (s, e) => ToggleRecents(_recentsButton.IsChecked == true);
        right.Children.Add(_recentsButton);

        Button themeButton = null;
        themeButton = Ui.IconBtn(Icons.Theme, "Tema: claro, oscuro o el de Windows", () => ShowThemeMenu(themeButton));
        right.Children.Add(themeButton);

        right.Children.Add(Ui.Divider());

        var save = Ui.Btn("Guardar", "Guardar como PNG o JPG (Ctrl+S)", Save, Icons.Save);
        save.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(save);

        var copy = Ui.Btn("Copiar", "Copiar la imagen anotada sin cerrar", () => CopyToClipboard(), Icons.Copy);
        copy.Margin = new Thickness(0, 0, 8, 0);
        right.Children.Add(copy);

        var copyClose = Ui.Btn("Copiar y cerrar", "Copiar y cerrar (Ctrl+C o Ctrl+Enter)",
            () => { if (CopyToClipboard()) Close(); }, Icons.Check, "Primario");
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
        b.SetResourceReference(StyleProperty, "Herramienta");
        b.Click += (s, e) => SetTool(t);
        _toolButtons[t] = b;
        return b;
    }

    void ShowThemeMenu(Button target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        foreach (var (mode, text) in new[]
        {
            (ModoTema.Sistema, "Usar el tema de Windows"),
            (ModoTema.Claro, "Claro"),
            (ModoTema.Oscuro, "Oscuro"),
        })
        {
            var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = AppSettings.Current.Tema == mode };
            var m = mode;
            item.Click += (s, e) => Theme.SetMode(m);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
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
        foreach (var (key, text) in new[] { ("Ctrl+Z", "Deshacer"), ("Shift", "45° y cuadrados"), ("1–4", "Grosor"), ("Ctrl+rueda", "Zoom") })
        {
            hints.Children.Add(Ui.Keycap(key));
            var t = new TextBlock { Text = text, Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            hints.Children.Add(t);
        }

        var zoom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(Ui.Btn("−", "Alejar (Ctrl+−)", () => SetZoom(_zoom.ScaleX / 1.15), style: "Plano"));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(Ui.Btn("+", "Acercar (Ctrl++)", () => SetZoom(_zoom.ScaleX * 1.15), style: "Plano"));
        zoom.Children.Add(Ui.Btn("Ajustar", "Ajustar a la ventana (Ctrl+0)", FitZoom, style: "Plano"));
        zoom.Children.Add(Ui.Btn("1:1", "Tamaño real", () => SetZoom(1 / DpiScale()), style: "Plano"));

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

        _ink.EditingMode = t switch
        {
            Tool.Pen or Tool.Highlighter => InkCanvasEditingMode.Ink,
            Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        // El puntero del sistema se oculta: lo reemplaza el de EditorWindow.Pointer.cs.
        _ink.UseCustomCursor = true;
        _ink.Cursor = Cursors.None;

        RefreshSwatches();
        UpdateDrawingAttributes();
        UpdatePointer();
    }

    void SetColor(Color color)
    {
        CurrentColor = color;
        RefreshSwatches();
        UpdateDrawingAttributes();
        if (_editing != null) _editing.Foreground = new SolidColorBrush(color);
        UpdatePointer();
    }

    void SetSize(double size)
    {
        _size = Math.Clamp(size, 1, 20);
        RefreshSizes();
        UpdateDrawingAttributes();
        if (_editing != null) _editing.FontSize = FontSizeFor(_size);
        UpdatePointer();
    }

    void RefreshSwatches()
    {
        var current = CurrentColor;
        foreach (var (b, c) in _swatches) b.IsChecked = c == current;
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
