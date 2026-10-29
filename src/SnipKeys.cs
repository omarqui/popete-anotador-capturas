// ----------------------------------------------------------------------------
//  Qué hace el hook de teclado con cada tecla (la parte sin Win32, con tests)
//
//  Win+Shift+S (sin Ctrl ni Alt) dispara la captura propia y la S no le llega a
//  Windows (ni abajo, ni repetida, ni arriba): así no abre Recortes. Mientras se
//  elige el recuadro, Esc cancela (la ventana de la selección puede no tener el
//  foco). El estado de los modificadores sale de los mismos eventos: las teclas
//  inyectadas (la de relleno de Popete, las de otros programas) no lo cambian.
// ----------------------------------------------------------------------------
enum SnipKeyAction { Pass, Swallow, Trigger, Cancel }

sealed class SnipKeys
{
    const int VK_S = 0x53, VK_ESCAPE = 0x1B;
    static readonly int[] WinKeys = { 0x5B, 0x5C };
    static readonly int[] ShiftKeys = { 0x10, 0xA0, 0xA1 };
    static readonly int[] CtrlKeys = { 0x11, 0xA2, 0xA3 };
    static readonly int[] AltKeys = { 0x12, 0xA4, 0xA5 };

    readonly HashSet<int> _held = new HashSet<int>(); // modificadores apretados
    readonly Func<int, bool> _isDown;
    readonly Func<bool> _ready;
    bool _sSwallowed;   // la S de abajo se tragó: la repetición y la de arriba también
    bool _escSwallowed;

    /// <param name="isDown">
    /// Si el sistema da la tecla por apretada (GetAsyncKeyState). El hook no ve todas las que se
    /// sueltan: Win+L suelta Win en el escritorio seguro, y con una ventana de administrador al frente
    /// Windows no le pasa sus teclas. Sin confirmar, una Win «trabada» haría que una S mayúscula
    /// cualquiera disparara la captura.
    /// </param>
    /// <param name="ready">Si Popete puede atender la captura ya (no está colgado): si no, la combinación le llega a Windows.</param>
    public SnipKeys(Func<int, bool> isDown = null, Func<bool> ready = null)
    {
        _isDown = isDown;
        _ready = ready;
    }

    /// <summary>Se está eligiendo el recuadro: Esc cancela.</summary>
    public bool Selecting { get; set; }

    public SnipKeyAction OnKey(int vk, bool down, bool injected)
    {
        if (injected) return SnipKeyAction.Pass;

        if (IsModifier(vk))
        {
            if (down) _held.Add(vk);
            else _held.Remove(vk);
            return SnipKeyAction.Pass;
        }

        if (vk == VK_S)
        {
            if (!down)
            {
                if (!_sSwallowed) return SnipKeyAction.Pass;
                _sSwallowed = false;
                return SnipKeyAction.Swallow;
            }
            if (_sSwallowed)
            {
                if (_isDown == null || _isDown(VK_S)) return SnipKeyAction.Swallow; // repetición
                _sSwallowed = false; // la S de arriba se soltó donde el hook no la vio
            }
            if (_isDown != null) _held.RemoveWhere(vk => !_isDown(vk)); // los que se soltaron sin verse
            if (!Any(WinKeys) || !Any(ShiftKeys) || Any(CtrlKeys) || Any(AltKeys)) return SnipKeyAction.Pass;
            if (_ready != null && !_ready()) return SnipKeyAction.Pass; // Popete ocupado: que abra Recortes
            _sSwallowed = true;
            return SnipKeyAction.Trigger;
        }

        if (vk == VK_ESCAPE)
        {
            if (!down)
            {
                if (!_escSwallowed) return SnipKeyAction.Pass;
                _escSwallowed = false;
                return SnipKeyAction.Swallow;
            }
            if (!Selecting) return SnipKeyAction.Pass;
            _escSwallowed = true;
            return SnipKeyAction.Cancel;
        }

        return SnipKeyAction.Pass;
    }

    static bool IsModifier(int vk) =>
        WinKeys.Contains(vk) || ShiftKeys.Contains(vk) || CtrlKeys.Contains(vk) || AltKeys.Contains(vk);

    bool Any(int[] keys) => keys.Any(_held.Contains);
}
