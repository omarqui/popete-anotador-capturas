// ----------------------------------------------------------------------------
//  Editor — barra de herramientas, barra de estado y selección de herramienta
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Grosores de la barra (teclas 1 a 3).</summary>
    static readonly double[] SizePresets = { 2, 4, 8 };

    readonly Dictionary<Tool, ToggleButton> _toolButtons = new Dictionary<Tool, ToggleButton>();
    readonly List<(ToggleButton button, Color color)> _swatches = new List<(ToggleButton, Color)>();
    readonly List<(ToggleButton button, double size)> _sizeButtons = new List<(ToggleButton, double)>();
    readonly TextBlock _sizeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, MinWidth = 34, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _saveText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBlock _zoomText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center };
    FrameworkElement _saveIcon;
    ToggleButton _recentsButton;
    Button _themeButton;
    Button _copyButton;
    TextBlock _copyLabel;

    // ------------------------------------------------------------------ barra de herramientas
    FrameworkElement BuildToolbar()
    {
        // Izquierda: se reparte en varias filas si la ventana es angosta.
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(BuildToolGroup()); // el usuario elige cuáles y en qué orden (EditorView.ToolOrder.cs)

        left.Children.Add(Ui.Divider());

        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (c, name) in Palette)
        {
            var sw = new ToggleButton { Background = new SolidColorBrush(c), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(sw, name);
            sw.SetResourceReference(StyleProperty, "EstiloMuestra");
            var color = c;
            sw.Click += (s, e) => SetColor(color);
            _swatches.Add((sw, c));
            colors.Children.Add(sw);
        }
        left.Children.Add(colors);

        left.Children.Add(Ui.Divider());

        var sizes = new List<UIElement>();
        string[] names = { "Fino", "Medio", "Grueso" };
        for (int i = 0; i < SizePresets.Length; i++)
        {
            double size = SizePresets[i];
            double dot = new[] { 4.0, 7, 10 }[i];
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
            ToolTip = "Numerar todo (N) · apagado: solo lo que tiene idea",
        };
        System.Windows.Automation.AutomationProperties.SetName(_numbersButton, "Numerar todo");
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

        // Guardar como no tiene botón: queda con Ctrl+S.
        // "Copiar y cerrar" o "Copiar", según AppSettings.CerrarAlCopiar (UpdateCopyButton)
        var copyClose = Ui.Btn("", "", CopyAndMaybeClose, Icons.Check, "EstiloPrimario");
        _copyButton = copyClose;
        _copyLabel = (TextBlock)((StackPanel)copyClose.Content).Children[1];
        UpdateCopyButton();
        Action onSettings = UpdateCopyButton;
        AppSettings.Changed += onSettings;
        Closed += (s, e) => AppSettings.Changed -= onSettings;
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

    // ------------------------------------------------------------------ otras herramientas
    // Las herramientas que no están en la barra (de fábrica: resaltador, línea, rectángulo, óvalo
    // y texto) y "texto con fondo" van en un menú para que la barra quede corta. El botón muestra
    // la herramienta del menú que esté activa (y queda marcado); si no hay ninguna, un ícono
    // genérico. Las teclas de todas siguen funcionando.
    ToggleButton _moreButton;

    ToggleButton BuildMoreTools()
    {
        _moreButton = new ToggleButton { Width = 48, Tag = MoreSlot, ContextMenu = BarContextMenu(null) };
        System.Windows.Automation.AutomationProperties.SetName(_moreButton, "Otras herramientas");
        _moreButton.SetResourceReference(StyleProperty, "EstiloHerramienta");

        var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = _moreButton };
        menu.Closed += (s, e) => UpdateMoreButton();
        _moreButton.Click += (s, e) =>
        {
            UpdateMoreButton(); // el clic no cambia la marca: la pone la herramienta activa
            FillMoreMenu(menu);
            menu.IsOpen = true;
        };
        UpdateMoreButton();
        return _moreButton;
    }

    /// <summary>Arma el menú "Otras" con lo que no está en la barra (cambia si se la reorganiza).</summary>
    void FillMoreMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        foreach (var (tool, icon, name, key, _) in MenuTools())
        {
            var item = new MenuItem
            {
                Header = MenuHeader(icon, name, key),
                IsChecked = _tool == tool,
                ToolTip = "Clic derecho: ponerla en la barra",
            };
            var t = tool;
            item.Click += (s, e) => SetTool(t);
            item.PreviewMouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                MoveToBar(t);
                FillMoreMenu(menu); // el menú sigue abierto, sin ella
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(MenuSeparator());

        // "Texto con fondo": los textos nuevos salen como etiqueta de color
        var label = new MenuItem
        {
            Header = MenuHeader(Icons.TextLabel, "Texto con fondo", ""),
            IsCheckable = true,
            IsChecked = AppSettings.Current.TextoConFondo,
            ToolTip = "Para los textos nuevos y los textos seleccionados",
        };
        label.Click += (s, e) => SetLabelMode(label.IsChecked);
        menu.Items.Add(label);

        menu.Items.Add(MenuSeparator());
        var hint = new TextBlock
        {
            Text = "Arrastra los botones de la barra para ordenarlos.\nClic derecho en una herramienta: pasarla a la barra o al menú.",
            FontSize = 11,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        menu.Items.Add(new MenuItem { Header = hint, IsEnabled = false });
    }

    static Separator MenuSeparator()
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(HeightProperty, 1.0);
        line.SetValue(MarginProperty, new Thickness(8, 4, 8, 4));
        line.SetResourceReference(Border.BackgroundProperty, "Borde");
        return new Separator { Template = new ControlTemplate(typeof(Separator)) { VisualTree = line } };
    }

    /// <summary>Ícono de la herramienta del menú activa (o el genérico) con una flechita.</summary>
    void UpdateMoreButton()
    {
        if (_moreButton == null) return;
        var inMenu = MenuTools().ToList();
        var names = inMenu.Select(m => $"{char.ToLower(m.name[0])}{m.name[1..]} ({m.key})").Append("texto con fondo");
        _moreButton.ToolTip = $"Otras herramientas: {string.Join(", ", names)}\nArrastra para cambiarlo de lugar";
        var active = inMenu.FirstOrDefault(m => m.tool == _tool);
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Ui.Icon(active.icon ?? Icons.Shapes));
        var chevron = Ui.Icon(Icons.ChevronDown, 12, 2);
        chevron.Margin = new Thickness(1, 2, 0, 0);
        content.Children.Add(chevron);
        _moreButton.Content = content;
        _moreButton.IsChecked = active.icon != null;
    }

    /// <summary>Fila del menú: ícono, nombre y tecla.</summary>
    static FrameworkElement MenuHeader(string icon, string name, string key)
    {
        var grid = new Grid { MinWidth = 180 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconEl = Ui.Icon(icon, 18);
        grid.Children.Add(iconEl);
        var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var keyText = new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        keyText.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        Grid.SetColumn(keyText, 2);
        grid.Children.Add(keyText);
        return grid;
    }

    /// <summary>
    /// "Texto con fondo": los textos nuevos salen como etiqueta de color. Si hay textos
    /// seleccionados, también se les aplica (con deshacer). El estado se recuerda en los ajustes.
    /// </summary>
    void SetLabelMode(bool on)
    {
        AppSettings.Current.TextoConFondo = on;
        try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }

        var changed = _selectedTexts.OfType<NoteText>().Where(nt => nt.Fondo != on).ToList();
        if (changed.Count > 0)
        {
            foreach (var nt in changed) nt.Fondo = on;
            PushUndo(new ActionChange(
                () => changed.ForEach(nt => nt.Fondo = !on),
                () => changed.ForEach(nt => nt.Fondo = on)));
            Dispatcher.InvokeAsync(UpdateSelectionVisual, DispatcherPriority.Loaded); // cambia de tamaño
        }
    }

    void UpdateThemeButton()
    {
        _themeButton.Content = Ui.Icon(Theme.IsDark ? Icons.Sun : Icons.Theme);
        var tip = Theme.IsDark ? "Cambiar a tema claro" : "Cambiar a tema oscuro";
        _themeButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(_themeButton, tip);
    }

    /// <summary>El botón dice lo que hace según "Cerrar el editor al copiar" (cambia en caliente desde Ajustes).</summary>
    void UpdateCopyButton()
    {
        bool close = AppSettings.Current.CerrarAlCopiar;
        _copyLabel.Text = close ? "Copiar y cerrar" : "Copiar";
        _copyButton.ToolTip = close
            ? "Copiar y cerrar (Ctrl+C, Ctrl+Enter o clic derecho en la captura)"
            : "Copiar (Ctrl+C o clic derecho en la captura; Ctrl+Enter copia y cierra)";
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
        foreach (var (key, text) in new[] { ("Ctrl+Z", "Deshacer"), ("Shift", "45° y cuadrados"), ("1–3", "Grosor"), ("Supr", "Borrar selección") })
        {
            hints.Children.Add(Ui.Keycap(key));
            var t = new TextBlock { Text = text, Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            hints.Children.Add(t);
        }

        var zoom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(Ui.Btn("−", "Alejar (Ctrl+−)", () => StepZoom(-1), style: "EstiloPlano"));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(Ui.Btn("+", "Acercar (Ctrl++)", () => StepZoom(1), style: "EstiloPlano"));
        zoom.Children.Add(Ui.Btn("Ajustar", "Ajustar a la ventana (Ctrl+0)", FitZoom, style: "EstiloPlano"));
        zoom.Children.Add(Ui.Btn("1:1", "Tamaño real", () => SetZoom(1 / DpiScale()), style: "EstiloPlano"));
        var settings = Ui.IconBtn(Icons.Settings, "Ajustes", () => SettingsWindow.Show(_shell));
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
        // Volver a tocar Recortar (botón o C) es como Enter: aplica y vuelve a la anterior
        if (t == Tool.Crop && _cropMode)
        {
            ConfirmCrop();
            return;
        }
        CommitText();
        var previous = _tool;
        _tool = t;
        foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;
        UpdateMoreButton();
        if (_cropMode && t != Tool.Crop) EndCropMode();      // aplica el marco si cambió
        if (!_cropMode && t == Tool.Crop) BeginCropMode(previous);

        // Elegir una herramienta de dibujo es para dibujar algo nuevo: se suelta la selección
        if (t != Tool.Select) ClearSelection();

        // Se recuerda para el próximo editor, también después de reiniciar
        if (t != Tool.Crop && AppSettings.Current.UltimaHerramienta != t)
        {
            AppSettings.Current.UltimaHerramienta = t;
            try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }
        }

        _ink.EditingMode = EditingModeFor(t);
        _ink.UseCustomCursor = true;
        _ink.Cursor = ToolCursor;

        RefreshSwatches();
        UpdateDrawingAttributes();
        UpdatePointer();
    }

    static InkCanvasEditingMode EditingModeFor(Tool t) => t switch
    {
        Tool.Pen or Tool.Highlighter => InkCanvasEditingMode.Ink,
        Tool.Eraser => InkCanvasEditingMode.EraseByStroke,
        _ => InkCanvasEditingMode.None,
    };

    // El puntero del sistema se oculta: lo reemplaza el de EditorView.Pointer.cs.
    // Seleccionar y Recortar usan los del sistema (Recortar los cambia según el borde).
    Cursor ToolCursor => _tool is Tool.Select or Tool.Crop ? Cursors.Arrow : Cursors.None;

    /// <param name="applyToSelection">Si hay trazos seleccionados, también se pintan (no en la rotación automática).</param>
    void SetColor(Color color, bool applyToSelection = true)
    {
        CurrentColor = color;
        RefreshSwatches();
        UpdateDrawingAttributes();
        if (_editing != null) _editing.Foreground = new SolidColorBrush(color);
        if (applyToSelection) ApplyColorToSelection(color);
        UpdatePointer();
    }

    // Rotación después de cada trazo: solo colores que se leen bien sobre capturas
    // (sin amarillo, negro ni blanco en el lápiz). El resaltador rota aparte.
    static readonly Color[] PenRotation = { Palette[0].color, Palette[3].color, Palette[2].color, Palette[4].color, Palette[5].color };
    static readonly Color[] HighlightRotation = { Palette[1].color, Palette[2].color, Palette[3].color };

    /// <summary>
    /// Después de agregar un trazo, el color pasa al siguiente de la rotación (si está activado
    /// en Ajustes). No toca el trazo recién hecho aunque siga seleccionado.
    /// </summary>
    void AdvanceColor()
    {
        if (!AppSettings.Current.RotarColores) return;
        var rotation = _tool == Tool.Highlighter ? HighlightRotation : PenRotation;
        int i = Array.IndexOf(rotation, CurrentColor); // -1 si se eligió a mano uno fuera de la rotación → el primero
        SetColor(rotation[(i + 1) % rotation.Length], applyToSelection: false);
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
