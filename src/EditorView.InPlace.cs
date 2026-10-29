// ----------------------------------------------------------------------------
//  Editor — la captura en el lugar (InPlaceWindow)
//
//  El mismo editor, con otra cara: la captura en el rectángulo de la pantalla
//  donde se tomó, a 1 píxel por píxel, sin esquinas ni sombra, con un borde de
//  Acento y una franja angosta alrededor donde también se puede dibujar. En
//  lugar de las barras de la pestaña, una barra flotante más corta
//  (EditorView.PlaceBar.cs; InPlaceLayout dice dónde). La tarjeta de las ideas
//  se mueve por todo el monitor. El zoom y el desplazamiento quedan dentro del
//  recuadro (EditorView.PlaceZoom.cs); los recientes pasan la captura a una
//  pestaña (OpenInTab), y UseTabLayout devuelve el diseño de siempre.
//
//  Todo se calcula desde píxeles (la captura y el monitor) con la escala del
//  monitor en que está la ventana, así un cambio de escala lo rehace.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Ancho de la franja dibujable alrededor de la captura en el lugar, en DIP.</summary>
    const double BandWidth = 48;

    /// <summary>Separación de la barra arrastrada al borde del área de trabajo.</summary>
    const double BarGap = 8;

    /// <summary>Separación entre la captura y la barra, que va encima de la franja (no tapa el borde de Acento, de 2 px).</summary>
    const double BarToCapture = 3;

    // Las partes del diseño de pestaña (el constructor las arma; aquí se mueven)
    readonly DockPanel _root = new DockPanel();
    readonly Grid _canvasArea = new Grid();
    FrameworkElement _toolbar, _statusBar;
    System.Windows.Shapes.Rectangle _limit;

    // En el lugar
    bool _inPlace;
    Int32Rect _placeCapture;         // la captura original en la pantalla, en píxeles
    MonitorInfo _placeMonitor;
    Rect _placeBand;                 // la franja, en DIP de la ventana
    Rect _placeVisible;              // la captura (lo visible, con el recorte), en DIP de la ventana
    Canvas _placeRoot;
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
            BeginPlaceView(); // zoom y desplazamiento propios (EditorView.PlaceZoom.cs)
            _host.HorizontalAlignment = HorizontalAlignment.Left;
            _host.VerticalAlignment = VerticalAlignment.Top;
            ResetPanShift();
            MountPlaceBar();

            _placeOutline = new System.Windows.Shapes.Rectangle { StrokeThickness = 2, IsHitTestVisible = false };
            _placeOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");

            // Sin fondo: fuera de la franja y de la barra la ventana es transparente y los clics pasan
            _placeRoot = new Canvas();
            _placeRoot.Children.Add(_canvasArea);
            _placeRoot.Children.Add(_placeOutline);
            _placeRoot.Children.Add(_floatingBar);
            _placeRoot.Children.Add(_ideaOverlay); // la tarjeta de las ideas puede ir a cualquier parte del monitor
            Content = _placeRoot;
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
        _placeRoot = null;
        UnmountPlaceBar();

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
        EndPlaceView();
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _host.HorizontalAlignment = HorizontalAlignment.Center;
        _host.VerticalAlignment = VerticalAlignment.Center;
        _host.Margin = new Thickness(ViewMargin);
        Content = _root;
        SetZoom(_zoom.ScaleX); // esquinas y modo de dibujo de la captura; FitZoom corre al cargarse en la pestaña
    }

    /// <summary>Ubica la franja, el borde, la captura con su zoom y la barra (al mostrarse, al recortar, con el zoom y al cambiar de escala).</summary>
    void LayoutInPlace()
    {
        if (!_inPlace) return;
        double s = DpiScale();
        if (Math.Abs(_zoom.ScaleX - _placeZoom / s) > 1e-9) SetZoom(_placeZoom / s); // a 100 %, 1 píxel de pantalla por píxel de la captura

        var m = _placeMonitor.Bounds;
        Rect Dip(Int32Rect r) => new Rect((r.X - m.X) / s, (r.Y - m.Y) / s, r.Width / s, r.Height / s);
        var monitor = Dip(m);
        var v = ShownRect; // en la tabla: la captura original empieza en (_pad, _pad)
        var visible = new Rect((_placeCapture.X - m.X + v.X - _pad) / s, (_placeCapture.Y - m.Y + v.Y - _pad) / s, v.Width / s, v.Height / s);
        _placeVisible = visible;
        _placeBand = InPlaceLayout.Band(visible, monitor, BandWidth);

        Canvas.SetLeft(_canvasArea, _placeBand.X);
        Canvas.SetTop(_canvasArea, _placeBand.Y);
        _canvasArea.Width = _placeBand.Width;
        _canvasArea.Height = _placeBand.Height;
        ApplyPlaceView(visible, new Point(v.X, v.Y), s); // la captura en el recuadro, con el zoom

        // El borde marca dónde está la captura en la pantalla (con zoom también): va por fuera: el trazo de un Rectangle se dibuja hacia adentro
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
        var p = _barMoved // la arrastró el usuario: se queda ahí, entera en el área de trabajo
            ? InPlaceLayout.Keep(_barWanted, _floatingBar.DesiredSize, work, BarGap)
            : InPlaceLayout.Bar(_placeVisible, _floatingBar.DesiredSize, work, BarToCapture);
        Canvas.SetLeft(_floatingBar, p.X);
        Canvas.SetTop(_floatingBar, p.Y);
    }

    /// <summary>Clic afuera, otra ventana al frente o una captura nueva: guarda la idea a medio escribir y copia.</summary>
    public bool CopyForExit() => CopyToClipboard();
}
