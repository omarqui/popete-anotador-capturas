// ----------------------------------------------------------------------------
//  Monitores en píxeles físicos (la app es PerMonitorV2: cada uno con su escala)
//
//  WPF habla en DIP, y un DIP mide distinto en cada monitor. Para ubicar algo
//  sobre un monitor que no es el de la ventana (el historial rápido en el
//  monitor del ratón, la captura en el lugar) se trabaja en píxeles y la
//  ventana se fija con SetWindowPos (PlaceInPixels).
// ----------------------------------------------------------------------------

/// <summary>Un monitor: su rectángulo y su área de trabajo en píxeles físicos, y su escala (DPI / 96).</summary>
sealed record MonitorInfo(Int32Rect Bounds, Int32Rect Work, double Scale)
{
    public bool Contains(int x, int y) =>
        x >= Bounds.X && x < Bounds.X + Bounds.Width && y >= Bounds.Y && y < Bounds.Y + Bounds.Height;
}

static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Todos los monitores, el principal primero.</summary>
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr hdc, ref RECT r, IntPtr d) =>
        {
            list.Add(Info(m));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.Bounds.X == 0 && m.Bounds.Y == 0).ToList();
    }

    /// <summary>El monitor que contiene el punto (en píxeles), o el más cercano.</summary>
    public static MonitorInfo At(int x, int y) => Info(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    /// <summary>La posición del ratón en píxeles físicos.</summary>
    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    /// <summary>El rectángulo que abarca todos los monitores, en píxeles.</summary>
    public static Int32Rect VirtualScreen()
    {
        var all = All();
        int left = all.Min(m => m.Bounds.X), top = all.Min(m => m.Bounds.Y);
        int right = all.Max(m => m.Bounds.X + m.Bounds.Width), bottom = all.Max(m => m.Bounds.Y + m.Bounds.Height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    static MonitorInfo Info(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
        return new MonitorInfo(ToRect(info.rcMonitor), ToRect(info.rcWork), scale);
    }

    static Int32Rect ToRect(RECT r) => new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    /// <summary>
    /// Fija la ventana en <paramref name="px"/> (píxeles físicos) apenas tiene HWND, antes de mostrarse.
    /// Se mueve dos veces: si cae en un monitor con otra escala, WPF reacomoda el tamaño al cambio de
    /// DPI de la primera, y la segunda deja el rectángulo exacto.
    /// </summary>
    public static void PlaceInPixels(Window w, Int32Rect px)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        void Place()
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            for (int i = 0; i < 2; i++)
                SetWindowPos(hwnd, IntPtr.Zero, px.X, px.Y, px.Width, px.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Place();
        else w.SourceInitialized += (s, e) => Place();
    }
}
