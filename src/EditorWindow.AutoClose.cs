// ----------------------------------------------------------------------------
//  Editor — cierre automático de capturas nuevas sin tocar
//
//  Una captura recién detectada en el portapapeles abre el editor; si solo se
//  quería pegar en otro lado, el editor estorba. Si el editor pierde el foco y
//  no se anotó nada, se cierra a los AutoCloseDelay segundos (volver antes lo
//  cancela). La captura se queda en el historial.
//  Cualquier cambio (trazo, texto, recorte), empezar a escribir o "Guardar como"
//  lo desactivan para siempre en esa ventana. No se cierra si tiene una ventana
//  propia abierta (Ajustes, una confirmación).
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(4);

    DispatcherTimer _autoCloseTimer;
    bool _touched;   // hubo alguna edición: ya no se cierra solo

    /// <summary>Lo llama CaptureHost para las capturas nuevas del portapapeles.</summary>
    public void EnableAutoClose()
    {
        _autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            if (!_touched && !IsActive && OwnedWindows.Count == 0) Close();
        };
        Deactivated += (s, e) => { if (!_touched) _autoCloseTimer.Start(); };
        Activated += (s, e) => _autoCloseTimer.Stop();
        // Si Windows no le dio el foco al abrir, cuenta como si ya se hubiera ido a otra ventana
        Loaded += (s, e) => Dispatcher.InvokeAsync(() =>
        {
            if (!IsActive && !_touched) _autoCloseTimer.Start();
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Hubo una edición: el editor ya no se cierra solo.</summary>
    void MarkTouched()
    {
        _touched = true;
        _autoCloseTimer?.Stop();
    }
}
