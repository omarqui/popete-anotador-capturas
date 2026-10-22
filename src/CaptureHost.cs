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

    readonly Application _app;
    readonly DispatcherTimer _debounce;
    readonly List<System.Drawing.Image> _menuThumbs = new List<System.Drawing.Image>();
    HwndSource _source;
    System.Windows.Forms.NotifyIcon _tray;
    System.Windows.Forms.ToolStripMenuItem _resumeItem;
    System.Windows.Forms.ToolStripMenuItem _pauseItem;
    System.Windows.Forms.ToolStripMenuItem _startupItem;
    HistoryWindow _historyWindow;
    bool _paused;

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
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_paused)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }

    void TryOpenFromClipboard()
    {
        var img = ClipboardHelper.TryGetImage();
        if (img == null) return;

        // Solo se abren imágenes nunca vistas. Algunos programas (Escritorio
        // remoto, historial del portapapeles, gestores de portapapeles...)
        // vuelven a publicar la misma imagen; esas se ignoran siempre.
        var sig = ClipboardHelper.Signature(img);
        if (History.IsKnown(sig)) return;

        OpenEditor(History.Create(img, sig));
        PruneHistory();
    }

    /// <summary>Aplica el límite de capturas del historial, sin tocar las que están abiertas.</summary>
    public static void PruneHistory() =>
        History.Prune(AppSettings.Current.MaxCapturas, s => EditorWindow.Open.ContainsKey(s.Meta.Id));

    public static void OpenEditor(Session s)
    {
        if (EditorWindow.Open.TryGetValue(s.Meta.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        try
        {
            new EditorWindow(s).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
            _historyWindow = new HistoryWindow(OpenEditor);
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
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (s, e) => _app.Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppInfo.TrayIcon(),
            Text = AppInfo.FullName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (s, e) =>
        {
            var last = History.List().FirstOrDefault();
            if (last != null) OpenEditor(last);
            else ShowHistory();
        };
        _tray.ShowBalloonTip(2500, AppInfo.FullName,
            "Activo. Toma una captura con Impr Pant o Win+Shift+S.", System.Windows.Forms.ToolTipIcon.Info);
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
            bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
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
        StartupHelper.Set(on);
        _startupItem.Checked = on;
    }

    public void Dispose()
    {
        if (_source != null)
        {
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
