// ----------------------------------------------------------------------------
//  La captura en el lugar: el editor sobre la pantalla, donde se tomó
//
//  Ventana sin marco, transparente y encima de todo, del tamaño del monitor. Lo
//  único que se ve es lo que dibuja el editor (EditorView.InPlace.cs): la
//  captura, su franja y la barra flotante. Lo transparente deja pasar los
//  clics al programa de abajo, y eso es «clic afuera»: la ventana pierde el
//  foco, copia y se cierra. Las ventanas que abre el editor (Ajustes, una
//  confirmación, Guardar como) no cuentan. Hay una sola a la vez (Current).
//
//  Pasar a pestaña (OpenInTab) muda el mismo EditorView a EditorShell, con su
//  deshacer y todo, y cierra esta ventana.
// ----------------------------------------------------------------------------
sealed class InPlaceWindow : Window, IEditorHost
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>La captura en el lugar abierta, si hay una.</summary>
    public static InPlaceWindow Current { get; private set; }

    readonly EditorView _view;
    readonly IntPtr _previous;   // la ventana que estaba al frente antes de Win+Shift+S
    bool _leaving;
    bool _movedToTab;            // el editor sigue vivo en una pestaña: no se cierra con la ventana
    int _ownModals;              // diálogos modales abiertos desde esta ventana («Guardar como», confirmaciones)

    /// <summary>
    /// Abre <paramref name="session"/> en su lugar: <paramref name="capturePx"/> es dónde estaba la
    /// captura, en píxeles de la pantalla, dentro de <paramref name="monitor"/>.
    /// </summary>
    public static void Open(Session session, MonitorInfo monitor, Int32Rect capturePx, IntPtr previousForeground)
    {
        FinishCurrent();
        var w = new InPlaceWindow(session, monitor, capturePx, previousForeground);
        Current = w;
        w.Show();
        w.Activate();
    }

    /// <summary>Una captura nueva o el historial rápido: la que esté abierta se copia y se cierra al instante.</summary>
    public static void FinishCurrent() => Current?.Finish();

    InPlaceWindow(Session session, MonitorInfo monitor, Int32Rect capturePx, IntPtr previousForeground)
    {
        _previous = previousForeground;
        Title = $"{AppInfo.Name} — captura en el lugar";
        Theme.Attach(this);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent; // los clics fuera de la franja y la barra pasan al programa de abajo
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        UseLayoutRounding = true;
        Width = monitor.Bounds.Width / monitor.Scale;
        Height = monitor.Bounds.Height / monitor.Scale;
        Monitors.PlaceInPixels(this, monitor.Bounds);

        _view = new EditorView(this, session);
        Content = _view;
        _view.ShowInPlace(capturePx, monitor);
        Loaded += (s, e) => _view.ShowInPlace(capturePx, monitor); // ya con la escala del monitor

        // Otra ventana al frente: se espera a que se acomode (un diálogo propio puede estar abriéndose)
        Deactivated += (s, e) => Dispatcher.InvokeAsync(CheckLeft, DispatcherPriority.ApplicationIdle);
        // Los modales se cuentan si se abren con esta ventana al frente: el modal de otra ventana de
        // Popete no debe dejarla sin cerrarse al hacer clic afuera
        ComponentDispatcher.EnterThreadModal += OnEnterModal;
        ComponentDispatcher.LeaveThreadModal += OnLeaveModal;
        // Alt+F4 (la ventana sin marco lo acepta): se cierra como con Esc, sin perder lo que se estaba escribiendo
        Closing += (s, e) =>
        {
            if (_leaving) return;
            _leaving = true;
            _view.PrepareClose();
        };
        Closed += (s, e) =>
        {
            ComponentDispatcher.EnterThreadModal -= OnEnterModal;
            ComponentDispatcher.LeaveThreadModal -= OnLeaveModal;
            if (!_movedToTab) _view.NotifyClosed();
            if (Current == this) Current = null;
        };
    }

    void OnEnterModal(object sender, EventArgs e)
    {
        if (IsActive) _ownModals++;
    }

    void OnLeaveModal(object sender, EventArgs e)
    {
        if (_ownModals > 0) _ownModals--;
    }

    /// <summary>Se fue a otro programa (clic afuera, Alt+Tab, Win): copiar y cerrar.</summary>
    void CheckLeft()
    {
        if (_leaving || IsActive || OwnedWindows.Count > 0 || _ownModals > 0) return;
        Finish();
    }

    /// <summary>Guarda la idea a medio escribir, copia y cierra, sin esperar el parpadeo.</summary>
    void Finish()
    {
        if (_leaving) return;
        _view.CopyForExit();
        Leave(restoreFocus: false);
    }

    /// <summary>Cierra la captura en el lugar (queda en el historial).</summary>
    void Leave(bool restoreFocus)
    {
        if (_leaving) return;
        _leaving = true;
        bool wasActive = IsActive;
        _view.PrepareClose();
        Close(); // Closed dispara NotifyClosed
        // Esc o Ctrl+Enter desde el editor: vuelve el programa de antes (con un clic afuera ya está al frente)
        if (restoreFocus && wasActive && _previous != IntPtr.Zero) SetForegroundWindow(_previous);
    }

    // ------------------------------------------------------------------ IEditorHost
    Window IEditorHost.Window => this;
    bool IEditorHost.InPlace => true;

    void IEditorHost.Dismiss(EditorView view) => Leave(restoreFocus: true);

    /// <summary>Soltar una imagen sobre la captura en el lugar: se abre en una pestaña.</summary>
    void IEditorHost.Replace(EditorView view, Session session) => CaptureHost.OpenEditor(session);

    void IEditorHost.OpenInTab(EditorView view)
    {
        // Después del evento que lo pidió (rueda, tecla, clic): el editor cambia de ventana
        Dispatcher.InvokeAsync(() =>
        {
            if (_leaving) return;
            _leaving = true;
            _movedToTab = true;
            _view.UseTabLayout();
            Content = null;
            EditorShell.Adopt(_view); // la ventana de pestañas pasa al frente mientras Popete todavía lo está
            Close();
        });
    }
}
