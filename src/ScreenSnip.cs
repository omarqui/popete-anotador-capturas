// ----------------------------------------------------------------------------
//  La selección propia de Win+Shift+S (modo de trabajo «En el lugar»)
//
//  Como Recortes en modo rectángulo: se congela la pantalla (una foto de todos
//  los monitores, antes de mostrar nada), cada monitor se cubre con su parte de
//  la foto oscurecida, y el recuadro que se arrastra se ve iluminado. Al soltar,
//  ese recuadro de la foto es la captura: va al historial y se abre en su lugar
//  (InPlaceWindow), y recién entonces se van las ventanas de la selección, así
//  no hay un parpadeo entre una cosa y la otra. El recuadro no sale del monitor
//  donde empezó. Esc o el clic derecho cancelan.
// ----------------------------------------------------------------------------
static class ScreenSnip
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    static extern int DwmFlush();

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);

    /// <summary>Lado mínimo del recuadro, en píxeles: menos es un clic sin arrastrar.</summary>
    const int MinSide = 5;

    static List<SnipWindow> _windows;
    static BitmapSource _frozen;      // la foto de todos los monitores
    static Int32Rect _virtual;        // dónde empieza la foto, en píxeles de la pantalla
    static IntPtr _previous;          // la ventana que estaba al frente

    /// <summary>Avisa cuándo empieza y termina la selección (el hook deja Esc para cancelar).</summary>
    public static event Action<bool> SelectingChanged;

    public static bool Active => _windows != null;

    public static void Start()
    {
        // Con un diálogo modal de Popete abierto, no: la selección quedaría anidada en su bucle
        if (Active || ComponentDispatcher.IsThreadModal) return;
        if (InPlaceWindow.Current != null)
        {
            InPlaceWindow.FinishCurrent();
            DwmFlush(); // que la captura anterior ya no esté en la pantalla al sacar la foto
        }
        _previous = GetForegroundWindow();
        _virtual = Monitors.VirtualScreen();
        try
        {
            _frozen = Grab(_virtual);
        }
        catch (Exception ex)
        {
            AppInfo.LogError(ex);
            _frozen = null;
            return;
        }
        _windows = Monitors.All().Select(m => new SnipWindow(m)).ToList();
        foreach (var w in _windows) w.Show();
        var (cx, cy) = Monitors.Cursor();
        (_windows.FirstOrDefault(w => w.Monitor.Contains(cx, cy)) ?? _windows[0]).Activate();
        SelectingChanged?.Invoke(true);
    }

    public static void Cancel()
    {
        if (!Active) return;
        bool ours = _windows.Any(w => w.IsActive);
        CloseWindows();
        if (ours && _previous != IntPtr.Zero) SetForegroundWindow(_previous);
    }

    /// <summary>La foto de <paramref name="r"/> (en píxeles de la pantalla), sin el cursor.</summary>
    static BitmapSource Grab(Int32Rect r)
    {
        using var bmp = new System.Drawing.Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(r.X, r.Y, 0, 0, new System.Drawing.Size(r.Width, r.Height), System.Drawing.CopyPixelOperation.SourceCopy);
        var h = bmp.GetHbitmap();
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
            return bgra;
        }
        finally
        {
            DeleteObject(h);
        }
    }

    /// <summary>La parte de la foto en <paramref name="px"/> (píxeles de la pantalla), como imagen propia.</summary>
    static BitmapSource Cut(Int32Rect px)
    {
        var part = new WriteableBitmap(new CroppedBitmap(_frozen, new Int32Rect(px.X - _virtual.X, px.Y - _virtual.Y, px.Width, px.Height)));
        part.Freeze();
        return part;
    }

    /// <summary>Se soltó el recuadro: guardar la captura y abrirla en su lugar.</summary>
    static void Done(MonitorInfo monitor, Int32Rect px)
    {
        var img = Cut(px);
        // Siempre una sesión nueva, aunque la imagen sea igual a otra: reabrir la vieja traería sus
        // anotaciones (o dos editores sobre la misma carpeta, si está en una pestaña)
        var session = History.Create(img, ClipboardHelper.Signature(img));
        CaptureHost.PruneHistory();
        try
        {
            InPlaceWindow.Open(session, monitor, px, _previous);
        }
        finally
        {
            CloseWindows();
        }
    }

    static void CloseWindows()
    {
        var windows = _windows;
        _windows = null;
        _frozen = null;
        foreach (var w in windows ?? new List<SnipWindow>()) w.Close();
        SelectingChanged?.Invoke(false);
    }

    // ------------------------------------------------------------------ un monitor
    /// <summary>Un monitor cubierto con su parte de la foto, oscurecida salvo el recuadro.</summary>
    sealed class SnipWindow : Window
    {
        public readonly MonitorInfo Monitor;
        readonly RectangleGeometry _hole = new RectangleGeometry();
        readonly System.Windows.Shapes.Rectangle _border = new System.Windows.Shapes.Rectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Point? _start;

        public SnipWindow(MonitorInfo monitor)
        {
            Monitor = monitor;
            Title = $"{AppInfo.Name} — elegir la captura";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            UseLayoutRounding = true;
            Cursor = Cursors.Cross;
            Width = monitor.Bounds.Width / monitor.Scale;
            Height = monitor.Bounds.Height / monitor.Scale;
            Monitors.PlaceInPixels(this, monitor.Bounds);

            var b = monitor.Bounds;
            var photo = new Image
            {
                Source = new CroppedBitmap(_frozen, new Int32Rect(b.X - _virtual.X, b.Y - _virtual.Y, b.Width, b.Height)),
                Stretch = Stretch.Fill,
            };
            RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.NearestNeighbor); // 1 píxel por píxel

            // Todo oscurecido menos el recuadro (EvenOdd: el recuadro es un agujero)
            var dim = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
                IsHitTestVisible = false,
                Data = new GeometryGroup
                {
                    FillRule = FillRule.EvenOdd,
                    Children = { new RectangleGeometry(new Rect(0, 0, 100000, 100000)), _hole },
                },
            };
            var marks = new Canvas { IsHitTestVisible = false };
            marks.Children.Add(_border);

            var root = new Grid();
            root.Children.Add(photo);
            root.Children.Add(dim);
            root.Children.Add(marks);
            Content = root;

            MouseLeftButtonDown += (s, e) =>
            {
                _start = e.GetPosition(this);
                CaptureMouse();
                Show(Selection(_start.Value));
            };
            MouseMove += (s, e) => { if (_start != null) Show(Selection(e.GetPosition(this))); };
            MouseLeftButtonUp += (s, e) =>
            {
                if (_start == null) return;
                var px = Pixels(_start.Value, e.GetPosition(this));
                _start = null;
                ReleaseMouseCapture();
                if (px.Width >= MinSide && px.Height >= MinSide) Done(Monitor, px);
                else Show(Rect.Empty); // un clic sin arrastrar: se sigue eligiendo
            };
            MouseRightButtonUp += (s, e) => Cancel();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Cancel();
            };
        }

        double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        /// <summary>
        /// El recuadro entre el comienzo del arrastre y <paramref name="p"/>, en píxeles de la pantalla,
        /// ajustado a píxeles enteros y sin salir de este monitor.
        /// </summary>
        Int32Rect Pixels(Point a, Point p)
        {
            double s = Scale;
            int w = Monitor.Bounds.Width, h = Monitor.Bounds.Height;
            int x0 = Math.Clamp((int)Math.Round(Math.Min(a.X, p.X) * s), 0, w), x1 = Math.Clamp((int)Math.Round(Math.Max(a.X, p.X) * s), 0, w);
            int y0 = Math.Clamp((int)Math.Round(Math.Min(a.Y, p.Y) * s), 0, h), y1 = Math.Clamp((int)Math.Round(Math.Max(a.Y, p.Y) * s), 0, h);
            return new Int32Rect(Monitor.Bounds.X + x0, Monitor.Bounds.Y + y0, x1 - x0, y1 - y0);
        }

        /// <summary>Lo que se va a capturar, en DIP de esta ventana (los mismos píxeles que Pixels).</summary>
        Rect Selection(Point p)
        {
            var px = Pixels(_start.Value, p);
            double s = Scale;
            return new Rect((px.X - Monitor.Bounds.X) / s, (px.Y - Monitor.Bounds.Y) / s, px.Width / s, px.Height / s);
        }

        void Show(Rect r)
        {
            _hole.Rect = r.IsEmpty ? Rect.Empty : r;
            _border.Visibility = r.IsEmpty || r.Width == 0 || r.Height == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (r.IsEmpty) return;
            // El borde va por fuera: no tapa ningún píxel de la captura
            Canvas.SetLeft(_border, r.X - 1);
            Canvas.SetTop(_border, r.Y - 1);
            _border.Width = r.Width + 2;
            _border.Height = r.Height + 2;
        }
    }
}
