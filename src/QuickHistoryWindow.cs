// ----------------------------------------------------------------------------
//  Historial rápido (atajo global; Alt+` de fábrica, se cambia en Ajustes)
//
//  Ventana sin barra de título, al estilo de Ditto o de Alt+Tab: a la izquierda
//  todas las capturas del historial (miniatura, hora y tamaño) y a la derecha la
//  elegida en grande, con sus anotaciones. Funciona aunque Popete esté solo en la
//  bandeja: el atajo lo registra CaptureHost.
//
//  Al abrir queda elegida la más reciente. Manteniendo los modificadores del
//  atajo, cada repetición de la tecla baja una (con Shift sube) y al soltarlos la
//  elegida se copia y se pega en la ventana que estaba al frente, como Ditto.
//  Si solo se tocó el atajo una vez, la ventana se queda abierta: flechas, rueda
//  o ratón para elegir; Enter o doble clic pegan; Esc o un clic fuera la cierran.
//  E (con o sin los modificadores) o el botón "Abrir en Popete" la abren en el editor.
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

    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUND = 2;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_V = 0x56;
    const uint KEYEVENTF_KEYUP = 0x2;

    const double ThumbWidth = 72, ThumbHeight = 44;
    const double ListWidth = 280;
    const double PreviewMargin = 16;
    /// <summary>Parte del área de trabajo del monitor que ocupa la ventana, sin bajar de 760×480.</summary>
    const double ScreenShare = 0.8, MinWindowWidth = 760, MinWindowHeight = 480;
    const int PageStep = 5;

    static readonly System.Globalization.CultureInfo Spanish = new System.Globalization.CultureInfo("es");

    readonly Hotkey _hotkey;
    readonly IntPtr _target;  // la ventana que estaba al frente al tocar el atajo: ahí se pega
    readonly List<Session> _sessions;
    readonly List<(Border row, Border mark)> _rows = new List<(Border, Border)>();
    readonly Image _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    readonly TextBlock _previewInfo = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly DispatcherTimer _releaseWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
    int _previewDecodeWidth; // ancho del panel de la vista previa en píxeles: decodificar más no se notaría
    int _index = -1;
    bool _repeated;   // se movió la selección manteniendo los modificadores: al soltarlos, se abre
    bool _done;       // ya se está cerrando (abrir el editor le quita el foco: que no cierre dos veces)

    QuickHistoryWindow(Hotkey hotkey)
    {
        _hotkey = hotkey;
        _target = GetForegroundWindow(); // antes de mostrarse
        Title = $"Historial rápido — {AppInfo.Name}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        PlaceOnMouseMonitor();
        Theme.Attach(this);

        _sessions = History.List();

        var header = BuildHeader();
        var preview = BuildPreview();
        Grid.SetColumn(preview, 1);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListWidth) });
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
    /// <summary>
    /// Ocupa <see cref="ScreenShare"/> del área de trabajo del monitor donde está el ratón y se centra en él.
    /// La app usa el DPI del sistema (no hay manifiesto que pida otro modo), así que los píxeles de
    /// Windows pasan a DIP con ese DPI en cualquier monitor.
    /// </summary>
    void PlaceOnMouseMonitor()
    {
        var work = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        double dpi = GetDpiForSystem() / 96.0;
        double left = work.Left / dpi, top = work.Top / dpi, w = work.Width / dpi, h = work.Height / dpi;
        Width = Math.Min(w, Math.Max(MinWindowWidth, w * ScreenShare));
        Height = Math.Min(h, Math.Max(MinWindowHeight, h * ScreenShare));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left + (w - Width) / 2;
        Top = top + (h - Height) / 2;
        // Lista, su borde, el marco de la ventana y los márgenes de la vista previa
        _previewDecodeWidth = (int)Math.Ceiling((Width - ListWidth - 3 - 2 * PreviewMargin) * dpi);
    }

    FrameworkElement BuildHeader()
    {
        var title = new TextBlock { Text = "Historial rápido", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text) in new[] { (_hotkey.Text, "siguiente"), ("Enter", "pegar"), ("E", "editar"), ("Esc", "cerrar") })
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
        bool abierta = EditorShell.IsOpen(s.Meta.Id);
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
            if (e.ClickCount == 2) PasteSelected();
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

        _previewInfo.VerticalAlignment = VerticalAlignment.Center;
        _previewInfo.TextAlignment = TextAlignment.Left; // el botón va a la derecha
        _preview.Margin = new Thickness(PreviewMargin, PreviewMargin, PreviewMargin, 10);
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);

        var open = Ui.Btn("Abrir en Popete", "Abrir en el editor para seguir anotando (E)", OpenSelected, Icons.Pen);
        open.Margin = new Thickness(12, 0, 0, 0);
        var footer = new DockPanel { Margin = new Thickness(PreviewMargin, 0, PreviewMargin, 12) };
        DockPanel.SetDock(open, Dock.Right);
        footer.Children.Add(open);
        footer.Children.Add(_previewInfo);

        DockPanel.SetDock(footer, Dock.Bottom);
        area.Children.Add(footer);
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
        _preview.Source = TryLoad(path, Math.Min(s.Meta.Width, _previewDecodeWidth));
        bool abierta = EditorShell.IsOpen(s.Meta.Id);
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
            case Key.Enter: PasteSelected(); break;
            case Key.E when _hotkey.Tecla != Key.E: OpenSelected(); break;
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
        PasteSelected();
    }

    // ------------------------------------------------------------------ pegar, abrir y cerrar
    /// <summary>Copia la elegida, con sus anotaciones, y la pega en la ventana que estaba al frente.</summary>
    void PasteSelected()
    {
        if (_done || _index < 0) return;
        _done = true;
        _releaseWatch.Stop();
        var s = _sessions[_index];
        bool copied;
        try
        {
            // Con ideas, como al copiar en el editor: las rutas de la imagen y de ideas.txt (Ideas.ForAi)
            var forAi = File.Exists(s.IdeasPath) && File.Exists(s.PreviewPath)
                ? Ideas.ForAi(s.PreviewPath, s.IdeasPath)
                : null;
            copied = forAi != null
                ? ClipboardHelper.SetText(forAi)
                : ClipboardHelper.SetPng(File.ReadAllBytes(File.Exists(s.PreviewPath) ? s.PreviewPath : s.OriginalPath), s);
        }
        catch (Exception) { copied = false; } // archivo borrado o dañado
        Close();
        if (copied) PasteInto(_target);
        else CaptureHost.Notify("No se pudo copiar la captura al portapapeles.");
    }

    /// <summary>
    /// Como Ditto: devuelve el foco a <paramref name="target"/> y le manda Ctrl+V cuando ya es la ventana
    /// activa y no queda ninguna tecla modificadora apretada (si no, llegaría Ctrl+Alt+V o Ctrl+Shift+V).
    /// Si no se da en 2 s, no pega: la imagen igual queda en el portapapeles. Tampoco pega en una ventana
    /// de Popete ni en una abierta como administrador (Windows no deja mandarle teclas).
    /// </summary>
    static void PasteInto(IntPtr target)
    {
        GetWindowThreadProcessId(target, out uint pid);
        if (target == IntPtr.Zero || pid == Environment.ProcessId) return;
        SetForegroundWindow(target);

        var deadline = DateTime.Now.AddSeconds(2);
        int ready = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (s, e) =>
        {
            bool modifiers = IsDown(VK_SHIFT) || IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);
            ready = GetForegroundWindow() == target && !modifiers ? ready + 1 : 0;
            if (ready >= 2) // dos vueltas seguidas: que la ventana termine de activarse
            {
                timer.Stop();
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            else if (DateTime.Now > deadline)
                timer.Stop();
        };
        timer.Start();
    }

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
