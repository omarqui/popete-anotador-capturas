// ----------------------------------------------------------------------------
//  Ventana del editor — barra de título propia, con las pestañas
//
//  WindowChrome quita la barra de Windows y deja la franja de arriba como zona
//  de arrastre (doble clic maximiza, clic derecho abre el menú de la ventana).
//  Lo que va en ella y debe recibir el ratón (pestañas, botones) lleva
//  WindowChrome.IsHitTestVisibleInChrome. Minimizar, maximizar y cerrar son
//  botones propios; para que Windows 11 muestre Snap Layouts al pasar sobre
//  maximizar, WM_NCHITTEST devuelve HTMAXBUTTON en ese botón, y su resaltado y su
//  clic se manejan con los mensajes "no cliente" (WM_NCMOUSEMOVE...).
//  Maximizada, Windows deja el borde de redimensionar fuera del monitor: se
//  compensa con un margen del mismo grosor.
// ----------------------------------------------------------------------------
sealed partial class EditorShell
{
    const double TitleBarHeight = 40;
    const double CaptionButtonWidth = 46;

    const int WM_NCHITTEST = 0x0084;
    const int WM_NCMOUSEMOVE = 0x00A0;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int WM_NCLBUTTONUP = 0x00A2;
    const int WM_NCMOUSELEAVE = 0x02A2;
    const int HTMAXBUTTON = 9;

    System.Windows.Shell.WindowChrome _chrome;
    Button _maxButton;
    TextBlock _maxGlyph;

    FrameworkElement BuildTitleBar()
    {
        var icon = new Image
        {
            Source = AppInfo.WindowIcon,
            Width = 16,
            Height = 16,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(CaptionButton("", "Minimizar", () => WindowState = WindowState.Minimized, "EstiloBotonVentana"));
        _maxButton = CaptionButton("", "Maximizar", ToggleMaximize, "EstiloBotonVentana");
        _maxGlyph = (TextBlock)_maxButton.Content;
        buttons.Children.Add(_maxButton);
        buttons.Children.Add(CaptionButton("", "Cerrar", Close, "EstiloCerrarVentana"));

        var bar = new DockPanel { Height = TitleBarHeight };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(icon);
        bar.Children.Add(buttons);
        bar.Children.Add(BuildTabStrip()); // EditorShell.Tabs.cs

        var border = new Border { Child = bar };
        border.SetResourceReference(Border.BackgroundProperty, "BarraTitulo");
        return border;
    }

    /// <summary>Botón de la barra de título con un glifo de Windows (Segoe Fluent Icons; MDL2 en Windows 10).</summary>
    static Button CaptionButton(string glyph, string tip, Action action, string style)
    {
        var text = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 };
        var b = new Button { Content = text, ToolTip = tip, Width = CaptionButtonWidth, Height = TitleBarHeight };
        b.SetResourceReference(StyleProperty, style);
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += (s, e) => action();
        return b;
    }

    void SetupChrome()
    {
        _chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = TitleBarHeight,
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            // Un píxel de marco de DWM: conserva la sombra y las esquinas redondeadas de Windows 11
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, _chrome);
        StateChanged += (s, e) => UpdateMaximizedLayout();
        // En Loaded, después de WindowChrome: el último gancho que se agrega es el primero que corre
        Loaded += (s, e) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(TitleBarHook);
        UpdateMaximizedLayout();
    }

    void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void UpdateMaximizedLayout()
    {
        bool max = WindowState == WindowState.Maximized;
        var border = SystemParameters.WindowResizeBorderThickness;
        _root.Margin = max ? border : new Thickness(0);
        _chrome.CaptionHeight = TitleBarHeight + (max ? border.Top : 0);
        _maxGlyph.Text = max ? "" : "";
        var tip = max ? "Restaurar" : "Maximizar";
        _maxButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(_maxButton, tip);
    }

    /// <summary>Snap Layouts: Windows trata el botón de maximizar como el suyo.</summary>
    IntPtr TitleBarHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (OverMaxButton(lParam))
                {
                    handled = true;
                    return new IntPtr(HTMAXBUTTON);
                }
                break;
            case WM_NCMOUSEMOVE:
                SetMaxHover(wParam.ToInt32() == HTMAXBUTTON);
                break;
            case WM_NCMOUSELEAVE:
                SetMaxHover(false);
                break;
            case WM_NCLBUTTONDOWN when wParam.ToInt32() == HTMAXBUTTON:
                handled = true; // sin esto, Windows dibuja su botón clásico
                break;
            case WM_NCLBUTTONUP when wParam.ToInt32() == HTMAXBUTTON:
                handled = true;
                SetMaxHover(false);
                ToggleMaximize();
                break;
        }
        return IntPtr.Zero;
    }

    bool OverMaxButton(IntPtr lParam)
    {
        if (_maxButton == null || !_maxButton.IsVisible) return false;
        long v = lParam.ToInt64();
        var screen = new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF)); // en píxeles
        var p = _maxButton.PointFromScreen(screen);
        return p.X >= 0 && p.Y >= 0 && p.X < _maxButton.ActualWidth && p.Y < _maxButton.ActualHeight;
    }

    /// <summary>El botón de maximizar no recibe el ratón de WPF (es "no cliente"): el resaltado va a mano.</summary>
    void SetMaxHover(bool on)
    {
        if (on) _maxButton.SetResourceReference(BackgroundProperty, "Hover");
        else _maxButton.ClearValue(BackgroundProperty);
    }
}
