// ----------------------------------------------------------------------------
//  Escucha del portapapeles + ícono en la bandeja
// ----------------------------------------------------------------------------
sealed class CaptureHost : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    const int WM_HOTKEY = 0x0312;
    const int QuickHistoryHotkeyId = 1;
    const uint MOD_NOREPEAT = 0x4000; // mantener la tecla no repite: cada toque baja una

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    static CaptureHost _current;

    readonly Application _app;
    readonly DispatcherTimer _debounce;
    readonly List<System.Drawing.Image> _menuThumbs = new List<System.Drawing.Image>();
    HwndSource _source;
    System.Windows.Forms.NotifyIcon _tray;
    System.Windows.Forms.ToolStripMenuItem _resumeItem;
    System.Windows.Forms.ToolStripMenuItem _pauseItem;
    System.Windows.Forms.ToolStripMenuItem _startupItem;
    HistoryWindow _historyWindow;
    UpdateChecker _updates;
    bool _paused;
    Hotkey _hotkey;        // el atajo del historial rápido registrado ahora (null = ninguno)
    bool _hotkeyPaused;    // Ajustes está grabando una combinación nueva

    public CaptureHost(Application app)
    {
        _app = app;
        // El portapapeles suele cambiar varias veces seguidas por captura: se agrupan.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (s, e) => { _debounce.Stop(); TryOpenFromClipboard(); };
    }

    public void Start()
    {
        PruneHistory();
        History.LoadKnownSignatures();

        // Ventana "message-only" invisible que recibe WM_CLIPBOARDUPDATE
        var p = new HwndSourceParameters("PopeteListener")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
        CreateTray();

        _current = this;
        RegisterQuickHistoryHotkey();
        AppSettings.Changed += RegisterQuickHistoryHotkey;
        if (HotkeyFailed)
            _tray.ShowBalloonTip(4000, AppInfo.Name,
                $"Otro programa ya usa {AppSettings.Current.AtajoHistorial.Text}, el atajo del historial rápido. Elige otro en Ajustes.",
                System.Windows.Forms.ToolTipIcon.Warning);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_paused)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        // El historial rápido funciona también con la detección en pausa
        else if (msg == WM_HOTKEY && wParam.ToInt32() == QuickHistoryHotkeyId && _hotkey != null)
        {
            QuickHistoryWindow.OnHotkey(_hotkey);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------ atajo del historial rápido
    /// <summary>El atajo de Ajustes no se pudo registrar: otro programa ya usa esa combinación.</summary>
    public static bool HotkeyFailed { get; private set; }

    /// <summary>Mientras Ajustes graba una combinación, el atajo actual no debe abrir el historial rápido.</summary>
    public static void PauseHotkey(bool paused)
    {
        if (_current == null) return;
        _current._hotkeyPaused = paused;
        _current.RegisterQuickHistoryHotkey();
    }

    /// <summary>Registra el atajo de los ajustes (corre también en cada AppSettings.Changed).</summary>
    void RegisterQuickHistoryHotkey()
    {
        var wanted = _hotkeyPaused ? null : AppSettings.Current.AtajoHistorial;
        if (wanted != null && !wanted.IsValid) wanted = null; // ajustes.json editado a mano
        if (wanted == _hotkey && !HotkeyFailed) return; // record: compara modificadores y tecla

        if (_hotkey != null) UnregisterHotKey(_source.Handle, QuickHistoryHotkeyId);
        _hotkey = null;
        HotkeyFailed = false;
        if (wanted == null) return;
        if (RegisterHotKey(_source.Handle, QuickHistoryHotkeyId, (uint)wanted.Modificadores | MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(wanted.Tecla)))
            _hotkey = wanted;
        else
            HotkeyFailed = true;
    }

    void TryOpenFromClipboard()
    {
        // Las imágenes copiadas de Excel, el navegador, etc. no son capturas: no abren el editor
        // (sí desde la bandeja, con "Abrir imagen del portapapeles").
        var img = ClipboardHelper.TryGetImage(onlyScreenshots: true);
        if (img == null) return;

        // Solo se abren imágenes nunca vistas. Algunos programas (Escritorio
        // remoto, historial del portapapeles, gestores de portapapeles...)
        // vuelven a publicar la misma imagen; esas se ignoran siempre.
        var sig = ClipboardHelper.Signature(img);
        if (History.IsKnown(sig)) return;

        OpenEditor(History.Create(img, sig), autoClose: true);
        PruneHistory();
    }

    // ------------------------------------------------------------------ arrastrar imágenes
    static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico" };

    /// <summary>¿Lo que se arrastra trae alguna imagen? (archivos de imagen o una imagen en sí)</summary>
    public static bool HasImages(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop)
            ? ((string[])data.GetData(DataFormats.FileDrop)).Any(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            : data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap);

    /// <summary>Las imágenes de lo que se soltó: archivos (Explorador) o una imagen (navegador, otras apps).</summary>
    static List<BitmapSource> ImagesFrom(IDataObject data)
    {
        var images = new List<BitmapSource>();
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            foreach (var f in (string[])data.GetData(DataFormats.FileDrop))
            {
                if (!ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                try { images.Add(History.LoadImage(f)); } catch { /* formato que Windows no sabe abrir */ }
            }
        }
        else if (data.GetData("PNG") is MemoryStream png && ClipboardHelper.TryDecode(png) is BitmapSource fromPng)
        {
            images.Add(fromPng);
        }
        else if (data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
        {
            images.Add(bmp); // también si el "PNG" no se pudo leer
        }
        return images;
    }

    /// <summary>
    /// Abre en el editor las imágenes soltadas. Si <paramref name="target"/> es un editor que
    /// todavía no tiene anotaciones, la primera lo reemplaza (misma pestaña); las demás, y
    /// todas si el editor ya tiene algo dibujado, se abren en pestañas nuevas.
    /// Una imagen que ya está en el historial abre esa sesión en vez de duplicarla.
    /// </summary>
    public static int OpenDropped(IDataObject data, EditorView target)
    {
        int opened = 0;
        foreach (var img in ImagesFrom(data))
        {
            var sig = ClipboardHelper.Signature(img);
            var session = History.FindBySignature(sig) ?? History.Create(img, sig);
            if (target != null && target.IsEmpty && !EditorShell.IsOpen(session.Meta.Id))
            {
                target.ReplaceWith(session);
                target = null; // solo la primera reusa la pestaña
            }
            else
            {
                OpenEditor(session);
            }
            opened++;
        }
        if (opened > 0) PruneHistory();
        return opened;
    }

    /// <summary>
    /// Aplica el límite de capturas del historial, sin tocar las que están en pestañas (abiertas, o
    /// guardadas para cuando se vuelva a abrir la ventana).
    /// </summary>
    public static void PruneHistory() =>
        History.Prune(AppSettings.Current.MaxCapturas, s => EditorShell.Keeps(s.Meta.Id));

    /// <summary>Abre la captura en una pestaña de la ventana del editor (o elige la que ya la tiene).</summary>
    /// <param name="autoClose">Captura nueva del portapapeles: se cierra sola si se deja sin tocar.</param>
    public static void OpenEditor(Session s, bool autoClose = false) => EditorShell.Open(s, autoClose);

    /// <summary>Globo en la bandeja: para avisos de ventanas que ya se cerraron (el historial rápido).</summary>
    public static void Notify(string text) =>
        _current?._tray.ShowBalloonTip(3000, AppInfo.Name, text, System.Windows.Forms.ToolTipIcon.Warning);

    void OpenFromClipboardManually()
    {
        var img = ClipboardHelper.TryGetImage();
        if (img == null)
        {
            _tray.ShowBalloonTip(2000, AppInfo.Name, "No hay ninguna imagen en el portapapeles.",
                System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        var sig = ClipboardHelper.Signature(img);
        OpenEditor(History.FindBySignature(sig) ?? History.Create(img, sig));
        PruneHistory();
    }

    void ShowHistory()
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow(s => OpenEditor(s));
            _historyWindow.Closed += (s, e) => _historyWindow = null;
            _historyWindow.Show();
        }
        if (_historyWindow.WindowState == WindowState.Minimized) _historyWindow.WindowState = WindowState.Normal;
        _historyWindow.Activate();
    }

    void CreateTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        _resumeItem = new System.Windows.Forms.ToolStripMenuItem("Retomar captura");
        _resumeItem.DropDownItems.Add("(cargando…)"); // para que muestre la flecha del submenú
        _resumeItem.DropDownOpening += (s, e) => BuildResumeMenu();
        menu.Items.Add(_resumeItem);
        menu.Items.Add("Ver historial…", null, (s, e) => ShowHistory());
        menu.Items.Add("Abrir imagen del portapapeles", null, (s, e) => OpenFromClipboardManually());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("Pausar detección", null, (s, e) => TogglePause());
        _startupItem = new System.Windows.Forms.ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleStartup())
        {
            Checked = StartupHelper.IsEnabled(),
        };
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add("Ajustes…", null, (s, e) => SettingsWindow.Show(null));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        // "Iniciar con Windows" también se cambia desde Ajustes: se relee al abrir el menú
        menu.Opening += (s, e) => _startupItem.Checked = StartupHelper.IsEnabled();
        menu.Items.Add("Salir", null, (s, e) => _app.Shutdown());

        // Colores del tema también en el menú de la bandeja (es de WinForms: va aparte)
        menu.Font = new System.Drawing.Font("Segoe UI", 9.5f);
        menu.Padding = new System.Windows.Forms.Padding(2, 4, 2, 4);
        ApplyTrayTheme(menu);
        Theme.Changed += () => ApplyTrayTheme(menu);

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppInfo.TrayIcon(),
            Text = AppInfo.FullName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Un clic izquierdo basta; el derecho queda para el menú. Vuelve la ventana como quedó
        // (con la pestaña elegida); si no había pestañas, la captura más reciente.
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button != System.Windows.Forms.MouseButtons.Left) return;
            if (EditorShell.Reopen()) return;
            var last = History.List().FirstOrDefault();
            if (last != null) OpenEditor(last);
            else ShowHistory();
        };
        _tray.ShowBalloonTip(2500, AppInfo.FullName,
            "Activo. Toma una captura con Impr Pant o Win+Shift+S.", System.Windows.Forms.ToolTipIcon.Info);

        _updates = new UpdateChecker(_tray, menu); // aviso de versión nueva (solo desde la Store)
    }

    void ApplyTrayTheme(System.Windows.Forms.ContextMenuStrip menu)
    {
        var colors = new TrayColors(Theme.IsDark);
        menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(colors) { RoundedEdges = true };
        menu.BackColor = colors.Back;
        menu.ForeColor = colors.Fore;
        _resumeItem.DropDown.BackColor = colors.Back;
        _resumeItem.DropDown.ForeColor = colors.Fore;
    }

    /// <summary>Paleta del menú de la bandeja: la misma del tema claro/oscuro de las ventanas.</summary>
    sealed class TrayColors : System.Windows.Forms.ProfessionalColorTable
    {
        public readonly System.Drawing.Color Back, Fore, Hover, Border, Line;

        public TrayColors(bool dark)
        {
            UseSystemColors = false;
            Back = dark ? System.Drawing.Color.FromArgb(0x32, 0x33, 0x38) : System.Drawing.Color.White;
            Fore = dark ? System.Drawing.Color.FromArgb(0xF3, 0xF3, 0xF3) : System.Drawing.Color.FromArgb(0x1B, 0x1B, 0x1F);
            Hover = dark ? System.Drawing.Color.FromArgb(0x44, 0x46, 0x4D) : System.Drawing.Color.FromArgb(0xEB, 0xED, 0xF0);
            Border = dark ? System.Drawing.Color.FromArgb(0x50, 0x52, 0x59) : System.Drawing.Color.FromArgb(0xD1, 0xD4, 0xDA);
            Line = dark ? System.Drawing.Color.FromArgb(0x3D, 0x3E, 0x44) : System.Drawing.Color.FromArgb(0xE1, 0xE3, 0xE8);
        }

        public override System.Drawing.Color ToolStripDropDownBackground => Back;
        public override System.Drawing.Color ImageMarginGradientBegin => Back;
        public override System.Drawing.Color ImageMarginGradientMiddle => Back;
        public override System.Drawing.Color ImageMarginGradientEnd => Back;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Hover;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override System.Drawing.Color CheckBackground => Hover;
        public override System.Drawing.Color CheckSelectedBackground => Hover;
        public override System.Drawing.Color CheckPressedBackground => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Back;
    }

    void BuildResumeMenu()
    {
        foreach (var img in _menuThumbs) img.Dispose();
        _menuThumbs.Clear();
        _resumeItem.DropDownItems.Clear();

        var sessions = History.List().Take(12).ToList();
        if (sessions.Count == 0)
        {
            _resumeItem.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("(sin capturas todavía)") { Enabled = false });
            return;
        }

        foreach (var s in sessions)
        {
            var session = s;
            bool abierta = EditorShell.IsOpen(s.Meta.Id);
            var item = new System.Windows.Forms.ToolStripMenuItem(
                $"{s.Meta.Updated:dd/MM HH:mm}   ·   {s.Meta.Width}×{s.Meta.Height}{(abierta ? "   (abierta)" : "")}",
                LoadMenuThumb(s.ThumbPath),
                (o, e) => OpenEditor(session))
            {
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
            };
            _resumeItem.DropDownItems.Add(item);
        }
        _resumeItem.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        _resumeItem.DropDownItems.Add("Ver historial completo…", null, (o, e) => ShowHistory());
    }

    System.Drawing.Image LoadMenuThumb(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var full = System.Drawing.Image.FromStream(ms);
            int h = 40;
            int w = Math.Max(1, full.Width * h / full.Height);
            if (w > 80) { w = 80; h = Math.Max(1, full.Height * w / full.Width); }
            var thumb = new System.Drawing.Bitmap(full, new System.Drawing.Size(w, h));
            _menuThumbs.Add(thumb);
            return thumb;
        }
        catch
        {
            return null;
        }
    }

    void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Reanudar detección" : "Pausar detección";
        _tray.Text = _paused ? AppInfo.Name + " (en pausa)" : AppInfo.FullName;
    }

    void ToggleStartup()
    {
        bool on = !StartupHelper.IsEnabled();
        bool now = StartupHelper.Set(on);
        _startupItem.Checked = now;
        if (on && !now) ConfirmDialog.Inform(null, "Iniciar con Windows", StartupHelper.BlockedMessage);
    }

    public void Dispose()
    {
        if (_source != null)
        {
            AppSettings.Changed -= RegisterQuickHistoryHotkey;
            if (_hotkey != null) UnregisterHotKey(_source.Handle, QuickHistoryHotkeyId);
            _hotkey = null;
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        foreach (var img in _menuThumbs) img.Dispose();
        _menuThumbs.Clear();
    }
}
