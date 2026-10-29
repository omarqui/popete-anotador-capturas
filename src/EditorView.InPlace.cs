// ----------------------------------------------------------------------------
//  Editor — la captura en el lugar (InPlaceWindow)
//
//  El mismo editor, con otra cara: la captura en el rectángulo de la pantalla
//  donde se tomó, a 1 píxel por píxel, sin esquinas ni sombra, con un borde de
//  Acento y una franja angosta alrededor donde también se puede dibujar. La
//  barra de herramientas y la de estado van juntas en una barra flotante
//  (InPlaceLayout dice dónde). La tarjeta de las ideas se mueve por todo el
//  monitor. No hay zoom ni desplazamiento: pedirlos pasa la captura a una
//  pestaña (OpenInTab), y UseTabLayout devuelve el diseño de siempre.
//
//  Todo se calcula desde píxeles (la captura y el monitor) con la escala del
//  monitor en que está la ventana, así un cambio de escala lo rehace.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Ancho de la franja dibujable alrededor de la captura en el lugar, en DIP.</summary>
    const double BandWidth = 48;

    /// <summary>Separación entre la franja y la barra flotante, y de la barra al borde del área de trabajo.</summary>
    const double BarGap = 8;

    // Las partes del diseño de pestaña (el constructor las arma; aquí se mueven)
    readonly DockPanel _root = new DockPanel();
    readonly Grid _canvasArea = new Grid();
    FrameworkElement _toolbar, _statusBar;
    System.Windows.Shapes.Rectangle _limit;
    StackPanel _hints;
    readonly List<UIElement> _zoomControls = new List<UIElement>();
    Button _tabButton;

    // En el lugar
    bool _inPlace;
    Int32Rect _placeCapture;         // la captura original en la pantalla, en píxeles
    MonitorInfo _placeMonitor;
    Rect _placeBand;                 // la franja, en DIP de la ventana
    Canvas _placeRoot;
    Border _floatingBar;
    System.Windows.Shapes.Rectangle _placeOutline;
    System.Windows.Media.Effects.Effect _frameShadow;

    /// <summary>La captura se está anotando en su lugar de la pantalla.</summary>
    bool InPlace => _inPlace;

    /// <summary>Pasa a otra ventana (de la captura en el lugar a una pestaña), tal como está.</summary>
    public void MoveTo(IEditorHost host) => _owner = host;

    /// <summary>
    /// Muestra la captura en su lugar: <paramref name="capturePx"/> es dónde estaba la captura original
    /// (en píxeles de la pantalla) y la ventana que aloja al editor cubre <paramref name="monitor"/> entero.
    /// </summary>
    public void ShowInPlace(Int32Rect capturePx, MonitorInfo monitor)
    {
        _placeCapture = capturePx;
        _placeMonitor = monitor;
        if (!_inPlace)
        {
            _inPlace = true;
            _root.Children.Clear();
            _canvasArea.Children.Remove(_ideaOverlay);

            _frameShadow = _framed.Effect;
            _framed.Effect = null; // la sombra correría la imagen de lo que hay debajo
            _limit.Visibility = Visibility.Collapsed;
            _scroll.SetResourceReference(BackgroundProperty, "FranjaLugar");
            _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _host.HorizontalAlignment = HorizontalAlignment.Left;
            _host.VerticalAlignment = VerticalAlignment.Top;
            ResetPanShift();
            _hints.Visibility = Visibility.Collapsed;
            foreach (var c in _zoomControls) c.Visibility = Visibility.Collapsed;
            _tabButton.Visibility = Visibility.Visible;

            var bars = new StackPanel();
            bars.Children.Add(_toolbar);
            bars.Children.Add(_statusBar);
            _floatingBar = new Border
            {
                Child = bars,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.3 },
            };
            _floatingBar.SetResourceReference(Border.BackgroundProperty, "Fondo");
            _floatingBar.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");
            _floatingBar.SizeChanged += (s, e) => PlaceFloatingBar();

            _placeOutline = new System.Windows.Shapes.Rectangle { StrokeThickness = 2, IsHitTestVisible = false };
            _placeOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");

            // Sin fondo: fuera de la franja y de la barra la ventana es transparente y los clics pasan
            _placeRoot = new Canvas();
            _placeRoot.Children.Add(_canvasArea);
            _placeRoot.Children.Add(_placeOutline);
            _placeRoot.Children.Add(_floatingBar);
            _placeRoot.Children.Add(_ideaOverlay); // la tarjeta de las ideas puede ir a cualquier parte del monitor
            Content = _placeRoot;
            UpdateCopyButton();
        }
        LayoutInPlace();
        Dispatcher.InvokeAsync(() => Focus(), DispatcherPriority.Input);
    }

    /// <summary>Vuelve al diseño de pestaña (barra arriba, estado abajo, lienzo con zoom).</summary>
    public void UseTabLayout()
    {
        if (!_inPlace) return;
        _inPlace = false;
        _placeRoot.Children.Clear();
        ((StackPanel)_floatingBar.Child).Children.Clear();
        _placeRoot = null;
        _floatingBar = null;

        _root.Children.Add(_toolbar);
        _root.Children.Add(_statusBar);
        _root.Children.Add(_recents);
        _root.Children.Add(_canvasArea);
        _canvasArea.Width = _canvasArea.Height = double.NaN;
        _ideaOverlay.Width = _ideaOverlay.Height = double.NaN;
        _canvasArea.Children.Add(_ideaOverlay);

        _framed.Effect = _frameShadow;
        _limit.Visibility = Visibility.Visible;
        _scroll.SetResourceReference(BackgroundProperty, "Lienzo");
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _host.HorizontalAlignment = HorizontalAlignment.Center;
        _host.VerticalAlignment = VerticalAlignment.Center;
        _host.Margin = new Thickness(ViewMargin);
        _hints.Visibility = Visibility.Visible;
        foreach (var c in _zoomControls) c.Visibility = Visibility.Visible;
        _tabButton.Visibility = Visibility.Collapsed;
        Content = _root;
        UpdateCopyButton();
        SetZoom(_zoom.ScaleX); // esquinas y modo de dibujo de la captura; FitZoom corre al cargarse en la pestaña
    }

    /// <summary>Ubica la franja, el borde y la barra (al mostrarse, al recortar y al cambiar de escala).</summary>
    void LayoutInPlace()
    {
        if (!_inPlace) return;
        double s = DpiScale();
        if (Math.Abs(_zoom.ScaleX - 1 / s) > 1e-9) SetZoom(1 / s); // 1 píxel de pantalla por píxel de la captura

        var m = _placeMonitor.Bounds;
        Rect Dip(Int32Rect r) => new Rect((r.X - m.X) / s, (r.Y - m.Y) / s, r.Width / s, r.Height / s);
        var monitor = Dip(m);
        var v = ShownRect; // en la tabla: la captura original empieza en (_pad, _pad)
        var visible = new Rect((_placeCapture.X - m.X + v.X - _pad) / s, (_placeCapture.Y - m.Y + v.Y - _pad) / s, v.Width / s, v.Height / s);
        _placeBand = InPlaceLayout.Band(visible, monitor, BandWidth);

        Canvas.SetLeft(_canvasArea, _placeBand.X);
        Canvas.SetTop(_canvasArea, _placeBand.Y);
        _canvasArea.Width = _placeBand.Width;
        _canvasArea.Height = _placeBand.Height;
        _host.Margin = new Thickness(visible.Left - _placeBand.Left, visible.Top - _placeBand.Top,
            _placeBand.Right - visible.Right, _placeBand.Bottom - visible.Bottom);

        // El borde va por fuera: el trazo de un Rectangle se dibuja hacia adentro
        var outline = visible;
        outline.Inflate(2, 2);
        Canvas.SetLeft(_placeOutline, outline.X);
        Canvas.SetTop(_placeOutline, outline.Y);
        _placeOutline.Width = outline.Width;
        _placeOutline.Height = outline.Height;

        _ideaOverlay.Width = monitor.Width;
        _ideaOverlay.Height = monitor.Height;
        PlaceFloatingBar();
    }

    void PlaceFloatingBar()
    {
        if (!_inPlace) return;
        double s = DpiScale();
        var m = _placeMonitor.Bounds;
        var w = _placeMonitor.Work;
        var work = new Rect((w.X - m.X) / s, (w.Y - m.Y) / s, w.Width / s, w.Height / s);
        _floatingBar.MaxWidth = Math.Max(200, work.Width - 2 * BarGap);
        _floatingBar.Measure(new Size(_floatingBar.MaxWidth, double.PositiveInfinity));
        var p = InPlaceLayout.Bar(_placeBand, _floatingBar.DesiredSize, work, BarGap);
        Canvas.SetLeft(_floatingBar, p.X);
        Canvas.SetTop(_floatingBar, p.Y);
    }

    /// <summary>Clic afuera, otra ventana al frente o una captura nueva: guarda la idea a medio escribir y copia.</summary>
    public bool CopyForExit() => CopyToClipboard();
}
