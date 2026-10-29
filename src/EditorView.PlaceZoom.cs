// ----------------------------------------------------------------------------
//  Editor — zoom y desplazamiento de la captura en el lugar
//
//  El recuadro (la captura con su franja) no se mueve ni cambia de tamaño: lo
//  de adentro se acerca o se aleja alrededor del ratón (Ctrl+rueda, Ctrl++/−),
//  por los mismos niveles que en la pestaña, también por debajo de 100 %. Con
//  zoom, el clic central y la rueda (Shift+rueda: de costado) mueven la vista;
//  a 100 % no, para que la captura no se despegue de su lugar sin querer.
//  Ctrl+0 o el botón del zoom en la barra la devuelven exacta a su lugar.
//
//  _placeZoom es el zoom real (1 = 1 píxel de pantalla por píxel de la
//  captura) y _placeShift lo que se corrió la esquina de la captura respecto de
//  su lugar, en DIP. LayoutInPlace los aplica: la esquina es el margen de _host
//  (el ScrollViewer tiene las barras ocultas, no deshabilitadas: así no limita
//  el tamaño de _host ni lo recorta, y su desplazamiento se mantiene en cero).
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Cuánto mueve la vista cada paso de la rueda, por unidad de Delta (120 por paso: 48 DIP).</summary>
    const double WheelPan = 0.4;

    double _placeZoom = 1;
    Vector _placeShift;
    Point _placeOrigin;              // la esquina de la captura en el recuadro a 100 %, en DIP
    Point? _placeTopLeft;            // la esquina de lo visible en la tabla, en píxeles (cambia al recortar)
    Vector _placePanFrom;
    Button _placeZoomButton;

    bool PlaceZoomed => Math.Abs(_placeZoom - 1) > 1e-9;

    /// <summary>Al pasar al lugar: tamaño real, en su lugar, y el desplazamiento del ScrollViewer siempre en cero.</summary>
    void BeginPlaceView()
    {
        ResetPlaceView();
        _placeTopLeft = null;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.ScrollChanged -= KeepPlaceScrollAtZero;
        _scroll.ScrollChanged += KeepPlaceScrollAtZero;
    }

    void EndPlaceView()
    {
        _scroll.ScrollChanged -= KeepPlaceScrollAtZero;
        ResetPlaceView();
    }

    // Con las barras ocultas el ScrollViewer igual se desplaza (la rueda, o al traer a la vista un
    // texto en edición): la vista se mueve solo con _placeShift.
    void KeepPlaceScrollAtZero(object sender, ScrollChangedEventArgs e)
    {
        if (!InPlace || _scroll.HorizontalOffset == 0 && _scroll.VerticalOffset == 0) return;
        _scroll.ScrollToHorizontalOffset(0);
        _scroll.ScrollToVerticalOffset(0);
    }

    void ResetPlaceView()
    {
        _placeZoom = 1;
        _placeShift = default;
    }

    /// <summary>
    /// Pone la captura donde dicen el zoom y el corrimiento (desde LayoutInPlace). <paramref name="visible"/>
    /// es la captura en su lugar a 100 %; <paramref name="topLeft"/>, su esquina en la tabla.
    /// </summary>
    void ApplyPlaceView(Rect visible, Point topLeft, double dpi)
    {
        // Al recortar cambia lo visible: con zoom, lo que se ve no tiene que saltar
        if (_placeTopLeft is Point old && old != topLeft) _placeShift += (old - topLeft) * (1 - _placeZoom) / dpi;
        _placeTopLeft = topLeft;

        _placeOrigin = new Point(visible.Left - _placeBand.Left, visible.Top - _placeBand.Top);
        var size = new Size(visible.Width * _placeZoom, visible.Height * _placeZoom);
        var origin = InPlaceLayout.KeepInView(_placeOrigin + _placeShift, size, _placeBand.Size);
        _placeShift = origin - _placeOrigin;
        _host.Margin = new Thickness(origin.X, origin.Y, 0, 0);

        if (_placeZoomButton != null)
        {
            _placeZoomButton.Content = $"{Math.Round(_placeZoom * 100)} %";
            _placeZoomButton.Visibility = PlaceZoomed ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Un nivel más cerca (+1) o más lejos (−1), dejando quieto el punto bajo el ratón (o el centro).</summary>
    void PlaceStepZoom(int direction, Point? anchor)
    {
        double r = _placeZoom;
        double next = direction > 0
            ? ZoomLevels.FirstOrDefault(l => l > r * 1.001, r)
            : ZoomLevels.LastOrDefault(l => l < r / 1.001, r);
        if (next == r) return;
        var cursor = anchor ?? new Point(_placeBand.Width / 2, _placeBand.Height / 2);
        var origin = InPlaceLayout.ZoomAt(_placeOrigin + _placeShift, cursor, r, next);
        _placeZoom = next;
        _placeShift = origin - _placeOrigin;
        LayoutInPlace();
    }

    /// <summary>El ratón sobre el recuadro, para acercar ahí con el teclado; si está afuera, null (el centro).</summary>
    Point? PlaceCursor()
    {
        var p = Mouse.GetPosition(_scroll);
        return p.X >= 0 && p.Y >= 0 && p.X <= _scroll.ActualWidth && p.Y <= _scroll.ActualHeight ? p : null;
    }

    /// <summary>La rueda en el lugar: Ctrl acerca o aleja; sin Ctrl, con zoom, mueve la vista.</summary>
    void PlaceWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) PlaceStepZoom(e.Delta > 0 ? 1 : -1, e.GetPosition(_scroll));
        else if (!PlaceZoomed) return;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PlacePanTo(_placeShift + new Vector(e.Delta * WheelPan, 0));
        else PlacePanTo(_placeShift + new Vector(0, e.Delta * WheelPan));
    }

    /// <summary>El clic central en el lugar: solo con zoom (a 100 % la captura no se despega de su lugar).</summary>
    bool BeginPlacePan()
    {
        if (!PlaceZoomed) return false;
        _placePanFrom = _placeShift;
        return true;
    }

    void PlacePanMove(Vector delta) => PlacePanTo(_placePanFrom + delta);

    void PlacePanTo(Vector shift)
    {
        _placeShift = shift;
        LayoutInPlace();
        PlaceIdeaCard(); // la tarjeta de la idea sigue a su número
    }
}
