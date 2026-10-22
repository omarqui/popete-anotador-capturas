// ----------------------------------------------------------------------------
//  Editor — barra de herramientas y selección de herramienta
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    // ------------------------------------------------------------------ toolbar
    FrameworkElement BuildToolbar()
    {
        var panel = new WrapPanel { Margin = new Thickness(6, 6, 6, 4) };

        panel.Children.Add(ToolButton("✏ Lápiz", Tool.Pen, "Lápiz (P)"));
        panel.Children.Add(ToolButton("🖍 Resaltador", Tool.Highlighter, "Resaltador / subrayador (H)"));
        panel.Children.Add(ToolButton("╱ Línea", Tool.Line, "Línea recta (L) — Shift: ángulos de 45°"));
        panel.Children.Add(ToolButton("➜ Flecha", Tool.Arrow, "Flecha (A) — Shift: ángulos de 45°"));
        panel.Children.Add(ToolButton("▭ Rectángulo", Tool.Rectangle, "Rectángulo (R) — Shift: cuadrado"));
        panel.Children.Add(ToolButton("T Texto", Tool.Text, "Texto (T) — clic para escribir"));
        panel.Children.Add(ToolButton("⌫ Borrador", Tool.Eraser, "Borrar trazos (E)"));

        panel.Children.Add(Divider());

        foreach (var c in Palette)
        {
            var sw = new Border
            {
                Width = 24, Height = 24,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(c),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var color = c;
            sw.MouseLeftButtonUp += (s, e) =>
            {
                CurrentColor = color;
                RefreshSwatches();
                UpdateDrawingAttributes();
                if (_editing != null) _editing.Foreground = new SolidColorBrush(color);
            };
            _swatches.Add((sw, c));
            panel.Children.Add(sw);
        }

        panel.Children.Add(Divider());

        panel.Children.Add(new TextBlock { Text = "Grosor", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        var slider = new Slider { Minimum = 1, Maximum = 20, Value = _size, Width = 110, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
        slider.ValueChanged += (s, e) =>
        {
            _size = e.NewValue;
            UpdateDrawingAttributes();
            if (_editing != null) _editing.FontSize = FontSizeFor(_size);
        };
        panel.Children.Add(slider);

        panel.Children.Add(Divider());

        panel.Children.Add(Ui.Btn("↶ Deshacer", "Deshacer (Ctrl+Z)", Undo));
        panel.Children.Add(Ui.Btn("🗑 Limpiar", "Quitar todas las anotaciones", ClearAll));
        panel.Children.Add(Ui.Btn("Ajustar", "Ajustar a la ventana (Ctrl+0)", FitZoom));
        panel.Children.Add(Ui.Btn("1:1", "Tamaño real", () => SetZoom(1 / DpiScale())));

        panel.Children.Add(Divider());

        panel.Children.Add(Ui.Btn("📋 Copiar", "Copiar imagen anotada sin cerrar", () => CopyToClipboard()));
        panel.Children.Add(Ui.Btn("💾 Guardar", "Guardar como PNG/JPG (Ctrl+S)", Save));
        panel.Children.Add(Ui.Btn("✔ Copiar y cerrar", "Copiar y cerrar (Ctrl+C o Ctrl+Enter)", () => { if (CopyToClipboard()) Close(); }));

        return new Border
        {
            Child = panel,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    ToggleButton ToolButton(string text, Tool t, string tip)
    {
        var b = new ToggleButton { Content = text, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
        b.Click += (s, e) => SetTool(t);
        _toolButtons[t] = b;
        return b;
    }

    static Border Divider() => new Border
    {
        Width = 1,
        Margin = new Thickness(6, 4, 6, 4),
        Background = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
    };

    void RefreshSwatches()
    {
        var current = CurrentColor;
        foreach (var (sw, c) in _swatches)
        {
            bool sel = c == current;
            sw.BorderThickness = new Thickness(sel ? 3 : 1);
            sw.BorderBrush = sel ? Brushes.DodgerBlue : Brushes.DimGray;
        }
    }

    // ------------------------------------------------------------------ herramientas
    void SetTool(Tool t)
    {
        CommitText();
        _tool = t;
        foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;

        switch (t)
        {
            case Tool.Pen:
            case Tool.Highlighter:
                _ink.EditingMode = InkCanvasEditingMode.Ink;
                _ink.UseCustomCursor = false;
                break;
            case Tool.Eraser:
                _ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                _ink.UseCustomCursor = false;
                break;
            default:
                _ink.EditingMode = InkCanvasEditingMode.None;
                _ink.UseCustomCursor = true;
                _ink.Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                break;
        }
        RefreshSwatches();
        UpdateDrawingAttributes();
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
