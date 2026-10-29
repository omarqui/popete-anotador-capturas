// ----------------------------------------------------------------------------
//  Editor — cierre automático de capturas nuevas sin tocar
//
//  Una captura recién detectada en el portapapeles abre una pestaña; si solo se
//  quería pegar en otro lado, estorba. Si la ventana pierde el foco y no se anotó
//  nada, se sale a los AutoCloseDelay segundos (volver antes lo cancela) con la
//  misma salida rápida que copiar y cerrar (EditorShell.Dismiss). La captura se
//  queda en el historial.
//  Cualquier cambio (trazo, texto, recorte), empezar a escribir, "Guardar como",
//  copiar sin cerrar o cambiar de pestaña a mano lo desactivan para siempre en esa
//  pestaña. No se cierra si la ventana tiene otra propia abierta (Ajustes, una
//  confirmación).
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(4);

    DispatcherTimer _autoCloseTimer;
    bool _touched;   // hubo alguna edición: ya no se cierra solo

    /// <summary>Lo llama EditorShell al crear la pestaña de una captura nueva del portapapeles.</summary>
    public void EnableAutoClose()
    {
        _autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            if (!_touched && !_owner.Window.IsActive && _owner.Window.OwnedWindows.Count == 0) Dismiss();
        };
        EventHandler deactivated = (s, e) => { if (!_touched) _autoCloseTimer.Start(); };
        EventHandler activated = (s, e) => _autoCloseTimer.Stop();
        _owner.Window.Deactivated += deactivated;
        _owner.Window.Activated += activated;
        Closed += (s, e) =>
        {
            _owner.Window.Deactivated -= deactivated;
            _owner.Window.Activated -= activated;
        };
        // Si Windows no le dio el foco a la ventana, cuenta como si ya se hubiera ido a otra
        Loaded += (s, e) => Dispatcher.InvokeAsync(() =>
        {
            if (!_owner.Window.IsActive && !_touched && !_closed) _autoCloseTimer.Start();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Hubo una edición: el editor ya no se cierra solo.</summary>
    void MarkTouched()
    {
        _touched = true;
        _autoCloseTimer?.Stop();
    }
}
