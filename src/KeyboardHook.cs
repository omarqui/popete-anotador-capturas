// ----------------------------------------------------------------------------
//  Win+Shift+S para Popete: hook de teclado de bajo nivel (WH_KEYBOARD_LL)
//
//  RegisterHotKey no sirve: Windows ya registró Win+Shift+S para Recortes. El
//  hook ve cada tecla de todo el sistema antes que Windows y SnipKeys decide.
//  Va en un hilo propio con su bucle de mensajes, nunca en el de WPF: Windows
//  espera la respuesta del hook en cada tecla, y si Popete estuviera ocupado
//  (guardando, exportando) el teclado de todos los programas se demoraría.
//  La callback solo decide y publica en el dispatcher.
//
//  Al tragar la S se inyecta una tecla sin asignar (0xE8, como PowerToys, con la marca
//  OwnMark para no tomarla como del teclado): con
//  Win apretada, Windows ve otra tecla y al soltar Win no abre el menú Inicio.
//  Con una ventana de administrador al frente Windows no le pasa sus teclas al
//  hook: ahí Win+Shift+S abre Recortes, como siempre.
// ----------------------------------------------------------------------------
sealed class KeyboardHook : IDisposable
{
    delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint WM_QUIT = 0x12;
    const uint LLKHF_INJECTED = 0x10;
    /// <summary>Marca (dwExtraInfo) de las teclas que inyecta Popete: solo esas se ignoran. Las de otros programas
    /// (un botón del mouse o un remapeo de PowerToys que manda Win+Shift+S) cuentan como del teclado.</summary>
    static readonly UIntPtr OwnMark = (UIntPtr)0x504F5045; // "POPE"
    const uint KEYEVENTF_KEYUP = 0x2;
    const byte VK_DUMMY = 0xE8; // sin asignar

    /// <summary>Sin latido del hilo de WPF por más que esto, Popete está colgado: Win+Shift+S queda para Recortes.</summary>
    const long HangMs = 1000;

    readonly SnipKeys _keys;
    readonly object _lock = new object();
    readonly DispatcherTimer _heartbeat;
    long _lastBeat;          // Environment.TickCount64 del último latido del hilo de WPF
    int _pending;            // hay un Win+Shift+S publicado que el hilo de WPF todavía no atendió
    readonly Dispatcher _dispatcher;
    readonly Action _onTrigger, _onCancel;
    readonly LowLevelKeyboardProc _proc; // en un campo: si el GC lo junta, Windows llamaría a la nada
    readonly Thread _thread;
    uint _threadId;
    IntPtr _hook;

    /// <summary>Si Windows aceptó el hook. Si no, Win+Shift+S sigue siendo de Recortes.</summary>
    public bool Installed { get; private set; }

    /// <summary>Se está eligiendo el recuadro: Esc cancela aunque la selección no tenga el foco.</summary>
    public bool Selecting
    {
        set { lock (_lock) _keys.Selecting = value; }
    }

    /// <param name="onTrigger">Win+Shift+S, en el hilo de WPF.</param>
    /// <param name="onCancel">Esc durante la selección, en el hilo de WPF.</param>
    public KeyboardHook(Action onTrigger, Action onCancel)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _onTrigger = () =>
        {
            Interlocked.Exchange(ref _pending, 0);
            onTrigger();
        };
        _onCancel = onCancel;
        _keys = new SnipKeys(vk => (GetAsyncKeyState(vk) & 0x8000) != 0, Ready);
        _proc = HookProc;

        // El hilo de WPF late cada 250 ms: si deja de latir, está trabado
        Volatile.Write(ref _lastBeat, Environment.TickCount64);
        _heartbeat = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _heartbeat.Tick += (s, e) => Volatile.Write(ref _lastBeat, Environment.TickCount64);
        _heartbeat.Start();

        // Sin using: si el hilo tardara más que la espera, Set() sobre un evento liberado tiraría el proceso
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Popete: Win+Shift+S" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Popete puede atender una captura: el hilo de WPF latió hace poco y no quedó otra sin atender.
    /// Si no, Win+Shift+S le llega a Windows y abre Recortes.
    /// </summary>
    bool Ready() =>
        Environment.TickCount64 - Volatile.Read(ref _lastBeat) < HangMs && Volatile.Read(ref _pending) == 0;

    void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        Installed = _hook != IntPtr.Zero;
        int error = Marshal.GetLastWin32Error();
        ready.Set();
        if (!Installed)
        {
            AppInfo.LogError(new System.ComponentModel.Win32Exception(error, "No se pudo instalar el hook de teclado para Win+Shift+S"));
            return;
        }
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } // el hook necesita un bucle de mensajes en su hilo
        UnhookWindowsHookEx(_hook);
    }

    IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            SnipKeyAction action;
            bool own = (k.flags & LLKHF_INJECTED) != 0 && k.dwExtraInfo == (IntPtr)(long)(ulong)OwnMark;
            lock (_lock) action = _keys.OnKey((int)k.vkCode, down, own);
            switch (action)
            {
                case SnipKeyAction.Trigger:
                    keybd_event(VK_DUMMY, 0, 0, OwnMark);
                    keybd_event(VK_DUMMY, 0, KEYEVENTF_KEYUP, OwnMark);
                    Interlocked.Exchange(ref _pending, 1);
                    _dispatcher.BeginInvoke(_onTrigger);
                    return new IntPtr(1);
                case SnipKeyAction.Cancel:
                    _dispatcher.BeginInvoke(_onCancel);
                    return new IntPtr(1);
                case SnipKeyAction.Swallow:
                    return new IntPtr(1);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        _heartbeat.Stop();
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
    }
}
