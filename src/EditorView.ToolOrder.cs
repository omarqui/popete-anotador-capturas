// ----------------------------------------------------------------------------
//  Editor — organizar la barra de herramientas
//
//  El usuario elige qué herramientas van a la vista y en qué orden:
//   - arrastrar un botón del grupo lo cambia de lugar (también "Otras herramientas");
//   - clic derecho en un botón: "Pasar a Otras herramientas" o "Restablecer la barra";
//   - clic derecho en una herramienta del menú "Otras": la pone en la barra.
//  Lo que no está en la barra va al menú. Se guarda en AppSettings.BarraHerramientas
//  (nombres de Tool y "Otras") y todos los editores abiertos se reacomodan.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Todas las herramientas, en el orden en que aparecen en el menú "Otras".</summary>
    static readonly (Tool tool, string icon, string name, string key, string tip)[] AllTools =
    {
        (Tool.Select, Icons.Select, "Seleccionar", "V", "clic en un trazo para cambiarle color o grosor; Supr lo borra"),
        (Tool.Pen, Icons.Pen, "Lápiz", "P", "Tab alterna con la flecha"),
        (Tool.Arrow, Icons.Arrow, "Flecha", "A", "sigue el mouse; casi recta se endereza; Shift: recta a 45°; Tab alterna con el lápiz"),
        (Tool.Highlighter, Icons.Highlighter, "Resaltador", "H", null),
        (Tool.Line, Icons.Line, "Línea", "L", "Shift: a 45°"),
        (Tool.Rectangle, Icons.Rectangle, "Rectángulo", "R", "Shift: cuadrado"),
        (Tool.Ellipse, Icons.Ellipse, "Óvalo", "O", "Shift: círculo"),
        (Tool.Text, Icons.Text, "Texto", "T", null),
        (Tool.Eraser, Icons.Eraser, "Borrador", "E", "borra trazos completos"),
        (Tool.Crop, Icons.Crop, "Recortar", "C", "arrastra los bordes de la captura; Enter o volver a tocarla aplica, Esc cancela"),
    };

    /// <summary>Lugar del botón "Otras herramientas" en la barra (siempre está).</summary>
    const string MoreSlot = "Otras";
    static readonly string[] DefaultBar = { nameof(Tool.Select), nameof(Tool.Pen), nameof(Tool.Arrow), MoreSlot, nameof(Tool.Eraser), nameof(Tool.Crop) };

    StackPanel _toolPanel;

    // Arrastre para reordenar
    FrameworkElement _toolDrag;  // botón agarrado (todavía puede ser un clic)
    double _toolDragDownX, _toolDragGrab;
    bool _toolDragging;

    /// <summary>La barra guardada, depurada: nombres conocidos, sin repetir y con "Otras".</summary>
    static List<string> BarSlots()
    {
        var saved = AppSettings.Current.BarraHerramientas ?? DefaultBar.ToList();
        var slots = saved
            .Where(s => s == MoreSlot || AllTools.Any(a => a.tool.ToString() == s))
            .Distinct().ToList();
        if (!slots.Contains(MoreSlot)) slots.Add(MoreSlot);
        return slots;
    }

    /// <summary>Las herramientas que no están en la barra: van al menú "Otras".</summary>
    static IEnumerable<(Tool tool, string icon, string name, string key, string tip)> MenuTools()
    {
        var slots = BarSlots();
        return AllTools.Where(a => !slots.Contains(a.tool.ToString()));
    }

    /// <summary>Grupo de herramientas de la barra. Se reacomoda cuando cambian los ajustes.</summary>
    FrameworkElement BuildToolGroup()
    {
        var group = Ui.Group();
        _toolPanel = (StackPanel)group.Child;
        BuildMoreTools();
        SyncToolPanel();
        HookToolDrag();

        Action onSettings = SyncToolPanel;
        AppSettings.Changed += onSettings;
        Closed += (s, e) => AppSettings.Changed -= onSettings;
        return group;
    }

    /// <summary>Pone en el grupo los botones de la barra guardada, en su orden (reusa los que ya hay).</summary>
    void SyncToolPanel()
    {
        if (_toolDragging) return;
        var desired = BarSlots().Select(s => s == MoreSlot ? (UIElement)_moreButton : ToolButton(Enum.Parse<Tool>(s))).ToList();
        // Primero se sacan los que sobran, así "Otras" no sale del árbol si su menú está abierto
        foreach (var old in _toolPanel.Children.Cast<UIElement>().Except(desired).ToList())
            _toolPanel.Children.Remove(old);
        for (int i = 0; i < desired.Count; i++)
        {
            int current = _toolPanel.Children.IndexOf(desired[i]);
            if (current == i) continue;
            if (current >= 0) _toolPanel.Children.RemoveAt(current);
            _toolPanel.Children.Insert(i, desired[i]);
        }
        foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == _tool;
        UpdateMoreButton();
    }

    ToggleButton ToolButton(Tool t)
    {
        if (_toolButtons.TryGetValue(t, out var existing)) return existing;
        var info = AllTools.First(a => a.tool == t);
        var name = $"{info.name} ({info.key})" + (info.tip != null ? " — " + info.tip : "");
        var b = new ToggleButton
        {
            Content = Ui.Icon(info.icon),
            ToolTip = name + "\nArrastra para cambiarla de lugar · clic derecho: pasarla a Otras herramientas",
            Tag = t.ToString(),
            ContextMenu = BarContextMenu(t),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.SetResourceReference(StyleProperty, "EstiloHerramienta");
        b.Click += (s, e) => SetTool(t);
        _toolButtons[t] = b;
        return b;
    }

    /// <summary>Clic derecho en un botón de la barra (<paramref name="tool"/> null: el de "Otras").</summary>
    ContextMenu BarContextMenu(Tool? tool)
    {
        var menu = new ContextMenu();
        if (tool is Tool t)
        {
            var toMenu = new MenuItem { Header = "Pasar a Otras herramientas" };
            toMenu.Click += (s, e) => MoveToMenu(t);
            menu.Items.Add(toMenu);
        }
        var reset = new MenuItem { Header = "Restablecer la barra", ToolTip = "Selección, lápiz, flecha, otras, borrador y recortar" };
        reset.Click += (s, e) => SaveBar(null);
        menu.Items.Add(reset);
        return menu;
    }

    void MoveToBar(Tool t)
    {
        var slots = BarSlots();
        slots.Insert(slots.IndexOf(MoreSlot), t.ToString()); // al lado de "Otras", de donde sale
        SaveBar(slots);
    }

    void MoveToMenu(Tool t)
    {
        var slots = BarSlots();
        slots.Remove(t.ToString());
        SaveBar(slots);
    }

    /// <param name="slots">null vuelve a la barra de fábrica.</param>
    void SaveBar(List<string> slots)
    {
        AppSettings.Current.BarraHerramientas = slots;
        try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }
        SyncToolPanel(); // Save ya avisa a los editores; por si falló al escribir
    }

    // ------------------------------------------------------------------ arrastrar para reordenar
    void HookToolDrag()
    {
        _toolPanel.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _toolDrag = SlotOf(e.OriginalSource as DependencyObject);
            _toolDragging = false;
            if (_toolDrag == null) return;
            _toolDragDownX = e.GetPosition(_toolPanel).X;
            _toolDragGrab = _toolDragDownX - LayoutInformation.GetLayoutSlot(_toolDrag).X;
        };
        _toolPanel.PreviewMouseMove += (s, e) =>
        {
            if (_toolDrag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndToolDrag();
                return;
            }
            double x = e.GetPosition(_toolPanel).X;
            if (!_toolDragging)
            {
                if (Math.Abs(x - _toolDragDownX) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
                // Empieza el arrastre: el grupo toma el mouse, así el botón no recibe el clic
                _toolDragging = true;
                _toolPanel.CaptureMouse();
                Panel.SetZIndex(_toolDrag, 1);
                _toolDrag.Opacity = 0.8;
            }
            DragToolTo(x);
            e.Handled = true;
        };
        _toolPanel.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_toolDragging) e.Handled = true;
            EndToolDrag();
        };
        // Solo cuando el grupo pierde el mouse: el botón también lo pierde al empezar el arrastre
        // (se lo quita el grupo) y ese aviso sube hasta acá
        _toolPanel.LostMouseCapture += (s, e) =>
        {
            if (e.OriginalSource == _toolPanel) EndToolDrag();
        };
    }

    /// <summary>El botón del grupo que contiene <paramref name="d"/>, o null.</summary>
    FrameworkElement SlotOf(DependencyObject d)
    {
        while (d != null)
        {
            var parent = VisualTreeHelper.GetParent(d);
            if (parent == _toolPanel) return d as FrameworkElement;
            d = parent;
        }
        return null;
    }

    /// <summary>El botón sigue al mouse; cuando su centro pasa el de un vecino, cambian de lugar.</summary>
    void DragToolTo(double x)
    {
        double width = LayoutInformation.GetLayoutSlot(_toolDrag).Width;
        double left = Math.Clamp(x - _toolDragGrab, 0, Math.Max(0, _toolPanel.ActualWidth - width));
        double center = left + width / 2;
        int index = _toolPanel.Children.Cast<FrameworkElement>()
            .Where(c => c != _toolDrag)
            .Count(c =>
            {
                var r = LayoutInformation.GetLayoutSlot(c);
                return r.X + r.Width / 2 < center;
            });
        if (_toolPanel.Children.IndexOf(_toolDrag) != index)
        {
            _toolPanel.Children.Remove(_toolDrag);
            _toolPanel.Children.Insert(index, _toolDrag);
            _toolPanel.UpdateLayout();
        }
        _toolDrag.RenderTransform = new TranslateTransform(left - LayoutInformation.GetLayoutSlot(_toolDrag).X, 0);
    }

    /// <summary>Suelta el botón en su lugar y, si hubo arrastre, guarda el orden.</summary>
    void EndToolDrag()
    {
        var dragged = _toolDrag;
        bool moved = _toolDragging;
        _toolDrag = null;
        _toolDragging = false;
        if (dragged == null || !moved) return;
        dragged.RenderTransform = null;
        dragged.Opacity = 1;
        Panel.SetZIndex(dragged, 0);
        if (_toolPanel.IsMouseCaptured) _toolPanel.ReleaseMouseCapture();
        SaveBar(_toolPanel.Children.Cast<FrameworkElement>().Select(c => (string)c.Tag).ToList());
    }
}
