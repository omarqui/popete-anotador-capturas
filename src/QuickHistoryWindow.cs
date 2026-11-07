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
//  Supr o la × de la fila la eliminan, con confirmación (no las abiertas en el editor).
// ----------------------------------------------------------------------------
sealed class QuickHistoryWindow : Window
{
    static QuickHistoryWindow _open;

    /// <summary>El atajo global: abre la ventana o, si ya está abierta, baja una.</summary>
    public static void OnHotkey(Hotkey hotkey)
    {
        // Como Win+Shift+S: la captura en el lugar se copia y se cierra primero, y se pega en el programa de abajo
        if (_open == null && !ComponentDispatcher.IsThreadModal) InPlaceWindow.FinishCurrent();
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
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

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
    readonly List<(Border row, Border mark, FrameworkElement delete)> _rows = new List<(Border, Border, FrameworkElement)>();
    readonly StackPanel _list = new StackPanel { Margin = new Thickness(6) };
    readonly TextBlock _notice = new TextBlock { FontSize = 12, Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Image _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    readonly TextBlock _previewInfo = new TextBlock { FontSize = 12, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly DispatcherTimer _releaseWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
    int _previewDecodeWidth; // ancho del panel de la vista previa en píxeles: decodificar más no se notaría
    int _index = -1;
    bool _repeated;   // se movió la selección manteniendo los modificadores: al soltarlos, se abre
    bool _done;       // ya se está cerrando (abrir el editor le quita el foco: que no cierre dos veces)
    bool _dragging;   // arrastrando una captura afuera (al Explorador): perder el foco no la cierra
    bool _confirming; // con la confirmación de eliminar abierta: el diálogo toma el foco y eso no la cierra
    Point? _dragDown; // dónde se apretó el botón sobre una fila o la vista previa

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
        Deactivated += (s, e) => { if (!_dragging && !_confirming) Cancel(); }; // un clic fuera la cierra
        PreviewMouseLeftButtonUp += (s, e) => _dragDown = null;
        Closed += (s, e) => _releaseWatch.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        if (_sessions.Count > 0) Select(0);
    }

    // ------------------------------------------------------------------ construcción
    /// <summary>
    /// Ocupa <see cref="ScreenShare"/> del área de trabajo del monitor donde está el ratón y se centra en él.
    /// Cada monitor tiene su escala (PerMonitorV2): se calcula en píxeles con la de ese monitor y
    /// la ventana se fija en píxeles (Monitors.PlaceInPixels).
    /// </summary>
    void PlaceOnMouseMonitor()
    {
        var (cx, cy) = Monitors.Cursor();
        var monitor = Monitors.At(cx, cy);
        var work = monitor.Work;
        double dpi = monitor.Scale;
        double w = work.Width / dpi, h = work.Height / dpi;
        Width = Math.Min(w, Math.Max(MinWindowWidth, w * ScreenShare));
        Height = Math.Min(h, Math.Max(MinWindowHeight, h * ScreenShare));
        int pw = (int)Math.Round(Width * dpi), ph = (int)Math.Round(Height * dpi);
        Monitors.PlaceInPixels(this, new Int32Rect(work.X + (work.Width - pw) / 2, work.Y + (work.Height - ph) / 2, pw, ph));
        // Lista, su borde, el marco de la ventana y los márgenes de la vista previa
        _previewDecodeWidth = (int)Math.Ceiling((Width - ListWidth - 3 - 2 * PreviewMargin) * dpi);
    }

    FrameworkElement BuildHeader()
    {
        var title = new TextBlock { Text = "Historial rápido", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, text) in new[] { (_hotkey.Text, "siguiente"), ("Enter", "pegar"), ("E", "editar"), ("Supr", "eliminar"), ("Esc", "cerrar") })
        {
            hints.Children.Add(Ui.Keycap(key));
            var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(5, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            hints.Children.Add(t);
        }

        _notice.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var dock = new DockPanel { Margin = new Thickness(16, 10, 4, 10) };
        DockPanel.SetDock(hints, Dock.Right);
        DockPanel.SetDock(title, Dock.Left);
        dock.Children.Add(hints);
        dock.Children.Add(title);
        dock.Children.Add(_notice); // avisos de eliminar, entre el título y las teclas

        // Fondo transparente (no nulo) para que toda la franja reciba el clic: arrastrarla mueve la ventana
        var bar = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, ToolTip = "Arrastra para mover la ventana" };
        bar.SetResourceReference(Border.BorderBrushProperty, "Borde");
        bar.MouseLeftButtonDown += (o, e) =>
        {
            if (e.ClickCount != 1 || _done) return;
            _repeated = false; // soltar los modificadores mientras se mueve no pega
            _releaseWatch.Stop();
            try { DragMove(); } catch (InvalidOperationException) { /* el botón ya se soltó */ }
            e.Handled = true;
        };
        return bar;
    }

    FrameworkElement BuildList()
    {
        foreach (var s in _sessions) _list.Children.Add(Row(s));

        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var border = new Border { Child = scroll, BorderThickness = new Thickness(0, 0, 1, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "Superficie");
        border.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return border;
    }

    /// <summary>Una fila por captura. Eliminar quita filas: su lugar se busca en cada evento.</summary>
    Border Row(Session s)
    {
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

        // × para eliminarla: se ve al pasar y en la elegida; las abiertas en el editor no la llevan
        FrameworkElement delete = null;
        if (!abierta)
        {
            var x = Ui.IconBtn(Icons.Close, "Eliminar del historial (Supr)", () => DeleteSession(s));
            x.Width = x.Height = 26;
            x.Margin = new Thickness(4, 0, 0, 0);
            x.Visibility = Visibility.Hidden;
            delete = x;
        }

        var line = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(thumb, Dock.Left);
        line.Children.Add(mark);
        line.Children.Add(thumb);
        if (delete != null)
        {
            DockPanel.SetDock(delete, Dock.Right);
            line.Children.Add(delete);
        }
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
        bool Selected() => _index >= 0 && _index < _sessions.Count && _sessions[_index] == s;
        row.MouseLeftButtonDown += (o, e) =>
        {
            if (e.ClickCount == 2) PasteSelected();
            else
            {
                Select(_sessions.IndexOf(s));
                _dragDown = e.GetPosition(this);
            }
            e.Handled = true;
        };
        row.MouseMove += (o, e) => MaybeStartDrag(e);
        row.MouseEnter += (o, e) =>
        {
            if (!Selected()) row.SetResourceReference(Border.BackgroundProperty, "Hover");
            if (delete != null) delete.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (o, e) =>
        {
            if (Selected()) return;
            row.Background = Brushes.Transparent;
            if (delete != null) delete.Visibility = Visibility.Hidden;
        };
        _rows.Add((row, mark, delete));
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
        // La vista previa también se arrastra (la elegida)
        _preview.MouseLeftButtonDown += (o, e) => { _dragDown = e.GetPosition(this); e.Handled = true; };
        _preview.MouseMove += (o, e) => MaybeStartDrag(e);

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
        if (_index >= 0 && _index < _rows.Count) Unmark(_index);
        _index = i;
        var (row, mark, delete) = _rows[i];
        row.SetResourceReference(Border.BackgroundProperty, "Seleccion");
        mark.Visibility = Visibility.Visible;
        if (delete != null) delete.Visibility = Visibility.Visible;
        row.BringIntoView();
        ShowPreview(_sessions[i]);
    }

    /// <summary>Le quita a una fila la marca de elegida (queda el resaltado si el ratón está encima).</summary>
    void Unmark(int i)
    {
        var (row, mark, delete) = _rows[i];
        bool over = row.IsMouseOver;
        if (over) row.SetResourceReference(Border.BackgroundProperty, "Hover");
        else row.Background = Brushes.Transparent;
        mark.Visibility = Visibility.Hidden;
        if (delete != null && !over) delete.Visibility = Visibility.Hidden;
    }

    // ------------------------------------------------------------------ eliminar
    /// <summary>
    /// Pide confirmación y borra la captura del historial; la fila se quita y queda elegida la que
    /// sigue. Mientras el diálogo está abierto, <see cref="_confirming"/> evita que perder el foco la
    /// cierre (el diálogo es de esta ventana, así que queda encima aunque ella sea Topmost).
    /// </summary>
    void DeleteSession(Session s)
    {
        int i = _sessions.IndexOf(s);
        if (_done || _confirming || i < 0) return;
        _repeated = false; // soltar los modificadores después de eliminar no pega la siguiente
        _releaseWatch.Stop();
        if (EditorShell.IsOpen(s.Meta.Id))
        {
            _notice.Text = "Está abierta en el editor: ciérrala para eliminarla";
            return;
        }

        bool ok;
        _confirming = true;
        try
        {
            ok = ConfirmDialog.Ask(this, "¿Eliminar esta captura?",
                "Se borra del historial con sus anotaciones, y no se puede deshacer.", "Eliminar");
        }
        finally { _confirming = false; }
        Activate(); // el foco vuelve aquí: las flechas, Enter y Supr siguen funcionando
        if (!ok) return;

        try { History.Delete(s); }
        catch (Exception ex)
        {
            _notice.Text = "No se pudo eliminar: " + ex.Message;
            return;
        }
        i = _sessions.IndexOf(s);
        if (i < 0) return;
        if (i == _index) Unmark(i);
        _sessions.RemoveAt(i);
        _rows.RemoveAt(i);
        _list.Children.RemoveAt(i);
        _notice.Text = "Se eliminó la captura";
        // Si era la elegida, pasa a la que seguía; si no (la × de otra fila), la elegida sigue siéndolo
        int next = _index > i ? _index - 1 : _index < i ? _index : Math.Min(i, _sessions.Count - 1);
        _index = -1;
        if (_sessions.Count > 0) Select(next);
        else
        {
            _preview.Source = null;
            _previewInfo.Text = "No quedan capturas en el historial.";
        }
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
            case Key.Delete when _hotkey.Tecla != Key.Delete:
                if (_index >= 0) DeleteSession(_sessions[_index]);
                break;
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
        if (!_repeated || _dragging || ModifiersHeld()) return;
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
            // Con ideas, como al copiar en el editor: las de ideas.txt, del último guardado
            var ideas = File.Exists(s.IdeasPath) && File.Exists(s.PreviewPath) ? File.ReadAllText(s.IdeasPath).Trim() : "";
            copied = ClipboardHelper.SetPng(File.ReadAllBytes(File.Exists(s.PreviewPath) ? s.PreviewPath : s.OriginalPath),
                s, ideas.Length > 0 ? ideas : null);
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

    // ------------------------------------------------------------------ arrastrar afuera
    /// <summary>Con el botón apretado sobre una fila o la vista previa, pasado el umbral del sistema, arrastra la elegida.</summary>
    void MaybeStartDrag(MouseEventArgs e)
    {
        if (_dragDown is not { } down || e.LeftButton != MouseButtonState.Pressed || _done || _dragging || _index < 0) return;
        var d = e.GetPosition(this) - down;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragDown = null;
        DragOut(_sessions[_index]);
    }

    /// <summary>
    /// Arrastra la captura (con sus anotaciones) como archivo: soltarla en una carpeta del Explorador o
    /// en el escritorio la guarda ahí; en un programa que acepte archivos, la adjunta. Se arrastra una
    /// copia con nombre propio («Captura 2026-11-07 104233.png»): todas se llaman anotada.png en el historial.
    /// La ventana tapa casi todo el monitor y está siempre encima, así que al salir de ella con el
    /// arrastre se oculta para dejar ver la carpeta de destino. Al soltar afuera se cierra.
    /// </summary>
    void DragOut(Session s)
    {
        string file;
        try { file = DragCopy(s); }
        catch (Exception) { CaptureHost.Notify("No se pudo preparar la captura para arrastrarla."); return; }

        _dragging = true;
        _repeated = false; // soltar los modificadores durante el arrastre no pega
        _releaseWatch.Stop();
        var hwnd = new WindowInteropHelper(this).Handle;
        void HideWhenOutside()
        {
            if (!IsVisible || !GetWindowRect(hwnd, out var r)) return;
            var (x, y) = Monitors.Cursor();
            if (x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) Hide();
        }
        QueryContinueDragEventHandler query = (o, e) => HideWhenOutside();
        GiveFeedbackEventHandler feedback = (o, e) => HideWhenOutside();
        QueryContinueDrag += query;
        GiveFeedback += feedback;

        DragDropEffects result;
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
            result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        catch (Exception) { result = DragDropEffects.None; }
        finally
        {
            QueryContinueDrag -= query;
            GiveFeedback -= feedback;
            _dragging = false;
        }

        // Soltada en algún lado, o cancelada ya fuera de la ventana (oculta): se cierra.
        // Cancelada adentro (Esc o soltada sobre la misma ventana): sigue abierta.
        if (result != DragDropEffects.None || !IsVisible) Cancel();
        else Activate();
    }

    /// <summary>Carpeta de las copias que se arrastran (fuera del historial, en la carpeta real de datos).</summary>
    static string DragFolder => Path.Combine(AppInfo.DataRoot, "Arrastre");

    /// <summary>
    /// Copia la imagen anotada (o la original si no hay) con un nombre legible. Borra las copias de
    /// arrastres anteriores con más de 10 minutos: el Explorador puede seguir copiando después de soltar.
    /// </summary>
    static string DragCopy(Session s)
    {
        Directory.CreateDirectory(DragFolder);
        foreach (var old in Directory.GetFiles(DragFolder))
        {
            try { if (File.GetLastWriteTime(old) < DateTime.Now.AddMinutes(-10)) File.Delete(old); }
            catch { /* en uso: la próxima vez */ }
        }
        var source = File.Exists(s.PreviewPath) ? s.PreviewPath : s.OriginalPath;
        var file = Path.Combine(DragFolder, $"Captura {s.Meta.Updated:yyyy-MM-dd HHmmss}.png");
        File.Copy(source, file, overwrite: true);
        File.SetLastWriteTime(file, DateTime.Now); // para la limpieza; File.Copy conserva la fecha del original
        return file;
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
