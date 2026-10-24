// ----------------------------------------------------------------------------
//  Ventana del editor: una sola, con una pestaña por captura
//
//  Las pestañas van en la barra de título (EditorShell.TitleBar.cs y
//  EditorShell.Tabs.cs). Cada pestaña tiene su EditorView, que se crea recién
//  cuando se la elige por primera vez (las restauradas no cargan nada hasta
//  entonces) y después queda vivo, oculto, hasta que se cierra la pestaña.
//
//  Restaurar: las pestañas abiertas y su orden se guardan en ventana.json en
//  cada cambio (ShellState). Al cerrar con la X quedan guardadas y vuelven la
//  próxima vez que se abre la ventana; "Cerrar todas" las vacía.
//
//  Salida rápida (copiar y cerrar, Esc, cierre automático): cierra la pestaña y,
//  si las que quedan son solo restauradas que nadie tocó, también la ventana
//  (Dismiss). Así, una captura nueva que abrió la ventana se copia y se vuelve
//  al programa de antes, como cuando cada captura era su propia ventana.
// ----------------------------------------------------------------------------
sealed partial class EditorShell : Window
{
    /// <summary>Una pestaña: su captura, su cabecera y, desde que se eligió por primera vez, su editor.</summary>
    sealed class Tab
    {
        public Session Session;
        public EditorView View;     // null hasta que se elige: las restauradas no cargan nada antes
        public Border Header;
        public Image Thumb;
        public TextBlock Text;
        public Button CloseButton;
        public bool Engaged;        // se abrió en esta ventana o se eligió a mano (no es solo restaurada)
        public bool AutoClose;      // captura nueva del portapapeles: se cierra sola si no se toca
    }

    static EditorShell _current;

    readonly List<Tab> _tabs = new List<Tab>();
    readonly Grid _content = new Grid();     // los editores creados; solo el elegido está visible
    readonly DockPanel _root = new DockPanel();
    Tab _selected;
    bool _closing;
    bool _clearOnClose;                       // "Cerrar todas": no queda nada para la próxima vez
    WindowState _restoreState = WindowState.Normal; // a qué vuelve si está minimizada

    // ------------------------------------------------------------------ desde afuera
    /// <summary>
    /// Abre <paramref name="s"/> en una pestaña (o elige la que ya la tiene) y trae la ventana al
    /// frente. Si la ventana estaba cerrada, se abre con las pestañas de la vez anterior.
    /// </summary>
    /// <param name="autoClose">Captura nueva del portapapeles: se cierra sola si se deja sin tocar.</param>
    public static void Open(Session s, bool autoClose = false)
    {
        if (!File.Exists(s.OriginalPath))
        {
            ConfirmDialog.Inform(_current, "No se pudo abrir la captura", "Ya no está en el historial.");
            return;
        }
        bool created = _current == null;
        if (created)
        {
            _current = new EditorShell();
            _current.Restore();
            _current.PlaceFor(s);
        }
        var shell = _current;
        var tab = shell.Find(s.Meta.Id);
        if (tab == null)
        {
            tab = shell.AddTab(s, shell._tabs.Count);
            tab.AutoClose = autoClose;
        }
        tab.Engaged = true;
        if (!shell.Select(tab, byUser: false))
        {
            // No llegó a mostrarse: sin esa pestaña no se abre (las restauradas siguen guardadas)
            if (created)
            {
                _current = null;
                shell.Close();
            }
            return;
        }
        if (created) shell.Show();
        shell.BringToFront();
        shell.SaveState();
    }

    public static bool IsOpen(string id) => _current?.Find(id) != null;

    /// <summary>Para el límite del historial: abiertas, o guardadas para cuando se vuelva a abrir la ventana.</summary>
    public static bool Keeps(string id) =>
        _current != null ? _current.Find(id) != null : ShellState.Current.Pestanas.Contains(id);

    /// <summary>Cierra la ventana como con la X: las pestañas quedan para la próxima vez.</summary>
    public static void CloseWindow() => _current?.Close();

    // ------------------------------------------------------------------ ventana
    EditorShell()
    {
        Title = AppInfo.Name;
        MinWidth = 640;
        MinHeight = 420;
        Theme.Attach(this);
        UseLayoutRounding = true;

        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        _root.Children.Add(titleBar);
        _root.Children.Add(_content);
        Content = _root;
        SetupChrome();

        PreviewKeyDown += OnShellKeyDown;
        // Alt solo no hace nada (nota 19 de AGENTS.md), aunque el foco no esté dentro de un editor
        PreviewKeyUp += (s, e) => { if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true; };
        StateChanged += (s, e) => { if (WindowState != WindowState.Minimized) _restoreState = WindowState; };
        Closing += (s, e) =>
        {
            _closing = true;
            foreach (var t in _tabs) t.View?.PrepareClose();
            SaveState();
            SaveBounds();
        };
        Closed += (s, e) =>
        {
            foreach (var t in _tabs) t.View?.NotifyClosed();
            if (_current == this) _current = null;
        };
    }

    /// <summary>Las pestañas de la vez anterior que sigan en el historial, sin cargarlas.</summary>
    void Restore()
    {
        var byId = History.List().ToDictionary(x => x.Meta.Id);
        foreach (var id in ShellState.Current.Pestanas)
            if (byId.TryGetValue(id, out var s) && Find(id) == null) AddTab(s, _tabs.Count);
    }

    /// <summary>Lugar al abrirse: el de la vez anterior o, la primera vez, a la medida de la captura.</summary>
    void PlaceFor(Session s)
    {
        var st = ShellState.Current;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // La barra de título tiene que caer en algún monitor (se pudo desconectar uno)
        if (st.Ancho > 0 && st.Alto > 0 && screen.IntersectsWith(new Rect(st.Izquierda, st.Arriba, st.Ancho, TitleBarHeight)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = st.Izquierda;
            Top = st.Arriba;
            Width = st.Ancho;
            Height = st.Alto;
            if (st.Maximizada) WindowState = WindowState.Maximized;
            return;
        }
        var work = SystemParameters.WorkArea;
        Width = Math.Min(work.Width * 0.85, Math.Max(760, s.Meta.Width + 80));
        Height = Math.Min(work.Height * 0.9, Math.Max(480, s.Meta.Height + 180));
        Width = Math.Max(Width, Math.Min(work.Width * 0.92, 1440)); // que la barra quepa en una fila
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    void SaveBounds()
    {
        var st = ShellState.Current;
        if (!IsLoaded) return; // nunca se mostró: se queda el lugar de antes
        var b = RestoreBounds; // sin maximizar ni minimizar
        if (!b.IsEmpty && b.Width > 0 && b.Height > 0)
        {
            st.Izquierda = b.Left;
            st.Arriba = b.Top;
            st.Ancho = b.Width;
            st.Alto = b.Height;
        }
        st.Maximizada = (WindowState == WindowState.Minimized ? _restoreState : WindowState) == WindowState.Maximized;
        st.Save();
    }

    /// <summary>Qué pestañas hay y en qué orden. En cada cambio: así sobrevive a un cierre inesperado.</summary>
    void SaveState()
    {
        var st = ShellState.Current;
        st.Pestanas = _clearOnClose ? new List<string>() : _tabs.Select(t => t.Session.Meta.Id).ToList();
        st.Save();
    }

    void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Activate();
        // Al frente aunque Popete esté en segundo plano
        Topmost = true;
        Topmost = false;
    }

    void UpdateTitle()
    {
        if (_selected == null) return;
        var m = _selected.Session.Meta;
        Title = $"{AppInfo.Name} — captura del {m.Created:dd/MM/yyyy HH:mm}"
            + (_tabs.Count > 1 ? $" ({_tabs.Count} pestañas)" : "");
    }

    // ------------------------------------------------------------------ pestañas
    Tab Find(string id) => _tabs.FirstOrDefault(t => t.Session.Meta.Id == id);

    Tab TabOf(EditorView view) => _tabs.FirstOrDefault(t => t.View == view);

    Tab AddTab(Session s, int index)
    {
        var tab = new Tab { Session = s };
        BuildHeader(tab);
        _tabs.Insert(index, tab);
        _strip.Children.Insert(index, tab.Header);
        LayoutTabs();
        UpdateTitle();
        return tab;
    }

    /// <summary>Saca la pestaña de la lista y de la pantalla (sin guardar ni avisar).</summary>
    void RemoveTab(Tab tab)
    {
        _tabs.Remove(tab);
        _strip.Children.Remove(tab.Header);
        if (tab.View != null) _content.Children.Remove(tab.View);
        LayoutTabs();
        UpdateTitle();
    }

    /// <summary>
    /// Pasa <paramref name="tab"/> al frente, creando su editor si hace falta. Si no se puede
    /// cargar, avisa, la quita y devuelve false (la elegida sigue siendo la de antes).
    /// </summary>
    /// <param name="byUser">La eligió el usuario (no es solo la que quedó al cerrar otra).</param>
    /// <param name="styleFrom">De quién toma herramienta, colores y grosor si no hay una elegida (se acaba de cerrar).</param>
    bool Select(Tab tab, bool byUser, EditorView styleFrom = null)
    {
        if (byUser) tab.Engaged = true;
        if (tab == _selected) return true;
        if (tab.View == null)
        {
            try
            {
                tab.View = new EditorView(this, tab.Session);
            }
            catch (Exception ex)
            {
                RemoveTab(tab);
                SaveState();
                ConfirmDialog.Inform(IsVisible ? this : null, "No se pudo abrir la captura", ex.Message);
                return false;
            }
            tab.View.Saved += () => LoadThumb(tab); // la miniatura de la pestaña sigue a las anotaciones
            if (tab.AutoClose) tab.View.EnableAutoClose();
            tab.View.Visibility = Visibility.Collapsed;
            _content.Children.Add(tab.View);
        }

        var previous = _selected;
        if (previous?.View != null)
        {
            previous.View.OnDeselected(byUser);
            previous.View.Visibility = Visibility.Collapsed;
        }
        _selected = tab;
        tab.View.Visibility = Visibility.Visible;
        tab.View.OnSelected(previous?.View ?? styleFrom);
        RefreshHeaders();
        BringHeaderIntoView(tab);
        UpdateTitle();
        return true;
    }

    /// <summary>Cierra una pestaña (la captura queda en el historial). Si era la última, cierra la ventana.</summary>
    void CloseTab(Tab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0 || _closing) return;
        tab.View?.PrepareClose();
        RemoveTab(tab);
        tab.View?.NotifyClosed();
        if (_tabs.Count == 0)
        {
            Close();
            return;
        }
        if (tab == _selected)
        {
            _selected = null;
            SelectNear(index, tab.View);
        }
        SaveState();
    }

    /// <summary>Elige la que quedó en <paramref name="index"/> (o la anterior), salteando las que no cargan.</summary>
    void SelectNear(int index, EditorView styleFrom)
    {
        while (_tabs.Count > 0 && !Select(_tabs[Math.Min(index, _tabs.Count - 1)], byUser: false, styleFrom)) { }
        if (_tabs.Count == 0) Close();
    }

    /// <summary>
    /// Salida rápida desde una pestaña (copiar y cerrar, Esc, cierre automático): la cierra y, si
    /// las que quedan son solo restauradas que nadie tocó, cierra también la ventana (quedan
    /// guardadas para la próxima vez).
    /// </summary>
    public void Dismiss(EditorView view)
    {
        var tab = TabOf(view);
        if (tab == null || _closing) return;
        CloseTab(tab);
        if (!_closing && !_tabs.Any(t => t.Engaged)) Close();
    }

    /// <summary>Soltar una imagen sobre una pestaña sin anotaciones: la nueva toma su lugar.</summary>
    public void Replace(EditorView view, Session session)
    {
        var old = TabOf(view);
        if (old == null) return;
        var tab = AddTab(session, _tabs.IndexOf(old) + 1);
        tab.Engaged = true;
        if (Select(tab, byUser: false)) CloseTab(old);
        SaveState();
    }

    // ------------------------------------------------------------------ teclado
    /// <summary>Ctrl+W cierra, Ctrl+Tab / Ctrl+Shift+Tab cambian, Ctrl+1…8 eligen esa y Ctrl+9 la última.</summary>
    void OnShellKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }
        if (_selected == null) return;
        var mods = Keyboard.Modifiers;
        int i = _tabs.IndexOf(_selected);
        if (mods == ModifierKeys.Control && e.Key == Key.W)
        {
            CloseTab(_selected);
        }
        else if ((mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Shift)) && e.Key == Key.Tab)
        {
            int step = mods.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            Select(_tabs[(i + step + _tabs.Count) % _tabs.Count], byUser: true);
        }
        else if (mods == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int n = e.Key == Key.D9 ? _tabs.Count - 1 : e.Key - Key.D1;
            if (n < _tabs.Count) Select(_tabs[n], byUser: true);
        }
        else return;
        e.Handled = true;
    }
}
