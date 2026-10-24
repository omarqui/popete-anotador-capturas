// ----------------------------------------------------------------------------
//  Historial rápido (atajo global; Alt+` de fábrica, se cambia en Ajustes)
//
//  Ventana sin barra de título, al estilo de Ditto o de Alt+Tab: a la izquierda
//  todas las capturas del historial (miniatura, hora y tamaño) y a la derecha la
//  elegida en grande, con sus anotaciones. Funciona aunque Popete esté solo en la
//  bandeja: el atajo lo registra CaptureHost.
//
//  Al abrir queda elegida la más reciente. Manteniendo los modificadores del
//  atajo, cada repetición de la tecla baja una (con Shift sube) y al soltarlos se
//  abre la elegida en el editor. Si solo se tocó el atajo una vez, la ventana se
//  queda abierta: flechas, rueda o ratón para elegir; Enter o doble clic abren;
//  Esc o un clic fuera la cierran.
// ----------------------------------------------------------------------------
sealed class QuickHistoryWindow : Window
{
    static QuickHistoryWindow _open;

    /// <summary>El atajo global: abre la ventana o, si ya está abierta, baja una.</summary>
    public static void OnHotkey(Hotkey hotkey)
    {
        if (_open != null)
        {
            _open.Step(+1);
            return;
        }
        _open = new QuickHistoryWindow(hotkey);
        _open.Closed += (s, e) => _open = null;
        _open.Show();
        _open.Activate(); // recibir el atajo le da a Popete permiso para pasar al frente
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUND = 2;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    const double ThumbWidth = 72, ThumbHeight = 44;
    const int PreviewDecodeWidth = 1200;
    const int PageStep = 5;

    static readonly System.Globalization.CultureInfo Spanish = new System.Globalization.CultureInfo("es");

    readonly Hotkey _hotkey;
    readonly List<Session> _sessions;
    readonly List<(Border row, Border mark)> _rows = new List<(Border, Border)>();
    readonly Image _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    readonly TextBlock _previewInfo = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly DispatcherTimer _releaseWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
    int _index = -1;
    bool _repeated;   // se movió la selección manteniendo los modificadores: al soltarlos, se abre
    bool _done;       // ya se está cerrando (abrir el editor le quita el foco: que no cierre dos veces)

    QuickHistoryWindow(Hotkey hotkey)
    {
        _hotkey = hotkey;
        Title = $"Historial rápido — {AppInfo.Name}";
        Width = 760;
        Height = 480;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; // en el monitor donde está el ratón
        Theme.Attach(this);

        _sessions = History.List();

        var header = BuildHeader();
        var preview = BuildPreview();
        Grid.SetColumn(preview, 1);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(BuildList());
        body.Children.Add(preview);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);

        var frame = new Border { Child = root, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");
        Content = frame;

        _releaseWatch.Tick += (s, e) => CheckRelease();
        SourceInitialized += (s, e) =>
        {
            int round = DWMWCP_ROUND; // esquinas redondeadas de Windows 11 (en Windows 10 no hace nada)
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
        Activated += (s, e) => Keyboard.Focus(this); // sin controles enfocables: las teclas llegan a la ventana
        Deactivated += (s, e) => Cancel(); // un clic fuera la cierra
        Closed += (s, e) => _releaseWatch.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        if (_sessions.Count > 0) Select(0);
    }

    // ------------------------------------------------------------------ construcción
    FrameworkElement BuildHeader()
    {
        var title = new TextBlock { Text = "Historial rápido", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text) in new[] { (_hotkey.Text, "siguiente"), ("Enter", "abrir"), ("Esc", "cerrar") })
        {
            hints.Children.Add(Ui.Keycap(key));
            var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(5, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            hints.Children.Add(t);
        }

        var dock = new DockPanel { Margin = new Thickness(16, 10, 4, 10) };
        DockPanel.SetDock(hints, Dock.Right);
        dock.Children.Add(hints);
        dock.Children.Add(title);

        var bar = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return bar;
    }

    FrameworkElement BuildList()
    {
        var panel = new StackPanel { Margin = new Thickness(6) };
        for (int i = 0; i < _sessions.Count; i++) panel.Children.Add(Row(i));

        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var border = new Border { Child = scroll, BorderThickness = new Thickness(0, 0, 1, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "Superficie");
        border.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return border;
    }

    Border Row(int i)
    {
        var s = _sessions[i];
        var thumb = new Image { Width = ThumbWidth, Height = ThumbHeight, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.HighQuality);
        // Las miniaturas se cargan después de mostrar la ventana, para que abra al instante
        Dispatcher.InvokeAsync(() =>
        {
            if (!_done) thumb.Source = TryLoad(s.ThumbPath, (int)ThumbWidth * 2);
        }, DispatcherPriority.Background);

        var when = new TextBlock { Text = When(s.Meta.Updated), TextTrimming = TextTrimming.CharacterEllipsis };
        bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
        var details = new TextBlock
        {
            Text = $"{s.Meta.Width}×{s.Meta.Height}{(abierta ? " · abierta" : "")}",
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        details.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        texts.Children.Add(when);
        texts.Children.Add(details);

        // Marca de la elegida: una píldora de acento a la izquierda, como las listas de Windows 11
        var mark = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden,
        };
        mark.SetResourceReference(Border.BackgroundProperty, "Acento");

        var line = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(thumb, Dock.Left);
        line.Children.Add(mark);
        line.Children.Add(thumb);
        line.Children.Add(texts);

        var row = new Border
        {
            Child = line,
            Padding = new Thickness(4, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        int index = i;
        row.MouseLeftButtonDown += (o, e) =>
        {
            if (e.ClickCount == 2) OpenSelected();
            else Select(index);
            e.Handled = true;
        };
        row.MouseEnter += (o, e) => { if (index != _index) row.SetResourceReference(Border.BackgroundProperty, "Hover"); };
        row.MouseLeave += (o, e) => { if (index != _index) row.Background = Brushes.Transparent; };
        _rows.Add((row, mark));
        return row;
    }

    FrameworkElement BuildPreview()
    {
        var area = new DockPanel();
        area.SetResourceReference(Panel.BackgroundProperty, "Lienzo");
        _previewInfo.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");

        if (_sessions.Count == 0)
        {
            _previewInfo.Text = "Todavía no hay capturas. Toma una con Impr Pant o Win+Shift+S.";
            _previewInfo.VerticalAlignment = VerticalAlignment.Center;
            _previewInfo.TextWrapping = TextWrapping.Wrap;
            _previewInfo.Margin = new Thickness(24);
            area.Children.Add(_previewInfo);
            return area;
        }

        _previewInfo.Margin = new Thickness(16, 0, 16, 12);
        _preview.Margin = new Thickness(16, 16, 16, 10);
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(_previewInfo, Dock.Bottom);
        area.Children.Add(_previewInfo);
        area.Children.Add(_preview);
        return area;
    }

    // ------------------------------------------------------------------ selección
    void Select(int i)
    {
        if (_rows.Count == 0) return;
        i = Math.Clamp(i, 0, _rows.Count - 1);
        if (_index >= 0)
        {
            var (oldRow, oldMark) = _rows[_index];
            if (oldRow.IsMouseOver) oldRow.SetResourceReference(Border.BackgroundProperty, "Hover");
            else oldRow.Background = Brushes.Transparent;
            oldMark.Visibility = Visibility.Hidden;
        }
        _index = i;
        var (row, mark) = _rows[i];
        row.SetResourceReference(Border.BackgroundProperty, "Seleccion");
        mark.Visibility = Visibility.Visible;
        row.BringIntoView();
        ShowPreview(_sessions[i]);
    }

    /// <summary>
    /// Baja (o sube) una, dando la vuelta al llegar a un extremo. Si los modificadores del
    /// atajo siguen apretados, soltarlos abre la elegida.
    /// </summary>
    void Step(int delta)
    {
        int n = _rows.Count;
        if (n > 0) Select(((_index + delta) % n + n) % n);
        if (ModifiersHeld())
        {
            _repeated = true;
            _releaseWatch.Start();
        }
    }

    void ShowPreview(Session s)
    {
        var path = File.Exists(s.PreviewPath) ? s.PreviewPath : s.OriginalPath;
        _preview.Source = TryLoad(path, Math.Min(s.Meta.Width, PreviewDecodeWidth));
        bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
        _previewInfo.Text = $"{Capitalize(s.Meta.Updated.ToString("dddd d 'de' MMMM, HH:mm", Spanish))}"
            + $"   ·   {s.Meta.Width}×{s.Meta.Height}{(abierta ? "   ·   abierta" : "")}";
    }

    // ------------------------------------------------------------------ teclado
    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key; // con Alt apretado, WPF manda Key.System
        switch (key)
        {
            case Key.Escape: Cancel(); break;
            case Key.Enter: OpenSelected(); break;
            case Key.Down: Step(+1); break;
            case Key.Up: Step(-1); break;
            case Key.PageDown: Select(_index + PageStep); break;
            case Key.PageUp: Select(_index - PageStep); break;
            case Key.Home: Select(0); break;
            case Key.End: Select(_rows.Count - 1); break;
            // Alt solo: que no active el menú de la ventana, que se tragaría la tecla siguiente
            case Key.LeftAlt: case Key.RightAlt: break;
            default:
                // La tecla del atajo con Shift (si el atajo no lo lleva) sube una
                if (key != _hotkey.Tecla) return;
                bool back = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !_hotkey.Modificadores.HasFlag(ModifierKeys.Shift);
                Step(back ? -1 : +1);
                break;
        }
        e.Handled = true;
    }

    void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true;
        CheckRelease(); // sin esperar al temporizador
    }

    /// <summary>¿Sigue apretado alguno de los modificadores del atajo? Se le pregunta a Windows: no depende del foco.</summary>
    bool ModifiersHeld()
    {
        var m = _hotkey.Modificadores;
        return (m.HasFlag(ModifierKeys.Control) && IsDown(VK_CONTROL))
            || (m.HasFlag(ModifierKeys.Alt) && IsDown(VK_MENU))
            || (m.HasFlag(ModifierKeys.Shift) && IsDown(VK_SHIFT));
    }

    static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    void CheckRelease()
    {
        if (!_repeated || ModifiersHeld()) return;
        _releaseWatch.Stop();
        OpenSelected();
    }

    // ------------------------------------------------------------------ abrir y cerrar
    void OpenSelected()
    {
        if (_done || _index < 0) return;
        _done = true;
        _releaseWatch.Stop();
        CaptureHost.OpenEditor(_sessions[_index]); // primero: así el editor pasa al frente en lugar de esta ventana
        Close();
    }

    void Cancel()
    {
        if (_done) return;
        _done = true;
        Close();
    }

    // ------------------------------------------------------------------ utilidades
    static BitmapSource TryLoad(string path, int decodeWidth)
    {
        try { return File.Exists(path) ? History.LoadImage(path, decodeWidth) : null; }
        catch { return null; } // archivo dañado: la fila queda sin imagen
    }

    /// <summary>"Hace 3 min", "Hoy 10:42", "Ayer 18:03", "Lunes 09:12" o la fecha.</summary>
    static string When(DateTime t)
    {
        var now = DateTime.Now;
        var ago = now - t;
        if (ago.TotalMinutes < 1) return "Hace un momento";
        if (ago.TotalMinutes < 60) return $"Hace {(int)ago.TotalMinutes} min";
        if (t.Date == now.Date) return $"Hoy {t:HH:mm}";
        if (t.Date == now.Date.AddDays(-1)) return $"Ayer {t:HH:mm}";
        if (ago.TotalDays < 7) return Capitalize(t.ToString("dddd HH:mm", Spanish));
        return t.ToString("dd/MM/yyyy HH:mm");
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Spanish) + s[1..];
}
