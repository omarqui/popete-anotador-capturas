// ----------------------------------------------------------------------------
//  Editor — numeración automática
//
//  Cada trazo (lápiz, resaltador, formas) y cada texto lleva un círculo con su
//  número de orden, pegado a donde empieza. Los números se recalculan en cada
//  cambio (MarkChanged): si se borra el 2, el 3 pasa a ser el 2.
//
//  El orden de creación se guarda en el propio trazo (propiedad del ISF) y en
//  TextNote.Orden, así sobrevive a cerrar y retomar la captura. Los círculos
//  están dentro de _surface: salen en la imagen exportada.
//  Se activan por captura con el botón "123" (o N); el valor inicial viene de
//  AppSettings.NumerarPorDefecto.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    static readonly Guid OrderProperty = new Guid("5b0e3c1e-8f6a-4d8e-9c1f-2a7d1f0b6c11");

    Canvas _numbersLayer;
    ToggleButton _numbersButton;
    long _nextOrder = 1;

    bool NumbersOn => _session.Meta.Numerar ?? AppSettings.Current.NumerarPorDefecto;

    Canvas BuildNumbersLayer()
    {
        _numbersLayer = new Canvas { IsHitTestVisible = false };
        return _numbersLayer;
    }

    static long OrderOf(Stroke s) =>
        s.ContainsPropertyData(OrderProperty) ? Convert.ToInt64(s.GetPropertyData(OrderProperty)) : 0;

    IEnumerable<TextBlock> TextBlocks => _textLayer.Children.OfType<TextBlock>();

    /// <summary>Después de cargar: el próximo número de orden sigue al más alto guardado.</summary>
    void InitOrders()
    {
        long max = 0;
        foreach (var s in _ink.Strokes) max = Math.Max(max, OrderOf(s));
        foreach (var tb in TextBlocks) if (tb.Tag is long o) max = Math.Max(max, o);
        _nextOrder = max + 1;
        AssignMissingOrders(); // sesiones viejas: en el orden en que estaban
    }

    void AssignMissingOrders()
    {
        foreach (var s in _ink.Strokes)
            if (s != _preview && !s.ContainsPropertyData(OrderProperty)) s.AddPropertyData(OrderProperty, _nextOrder++);
        foreach (var tb in TextBlocks)
            if (tb.Tag is not long) tb.Tag = _nextOrder++;
    }

    void ToggleNumbers()
    {
        _session.Meta.Numerar = !NumbersOn;
        _numbersButton.IsChecked = NumbersOn;
        MarkChanged();
    }

    void RefreshNumbers()
    {
        if (_numbersLayer == null) return;
        _numbersLayer.Children.Clear();
        if (_numbersButton != null) _numbersButton.IsChecked = NumbersOn;
        if (!NumbersOn) return;
        AssignMissingOrders();

        // (orden, punto de inicio, dirección hacia donde avanza, color)
        var items = new List<(long order, Point at, Vector away, Color color)>();
        foreach (var s in _ink.Strokes)
        {
            if (s == _preview || s.StylusPoints.Count == 0) continue;
            var p0 = s.StylusPoints[0].ToPoint();
            // Dirección inicial del trazo: el número va del lado contrario para no taparlo
            var dir = new Vector(-1, -1);
            foreach (var sp in s.StylusPoints)
            {
                var v = sp.ToPoint() - p0;
                if (v.Length > 12) { dir = v; break; }
            }
            dir.Normalize();
            items.Add((OrderOf(s), p0, -dir, s.DrawingAttributes.Color));
        }
        foreach (var tb in TextBlocks)
        {
            var color = (tb.Foreground as SolidColorBrush)?.Color ?? Palette[0].color;
            var at = new Point(Canvas.GetLeft(tb), Canvas.GetTop(tb) + tb.FontSize * 0.7);
            items.Add((tb.Tag is long o ? o : long.MaxValue, at, new Vector(-1, 0), color));
        }

        double r = NumberRadius;
        int n = 1;
        foreach (var item in items.OrderBy(i => i.order))
        {
            var center = item.at + item.away * (r + 4);
            var badge = NumberBadge(n++, item.color, r);
            Canvas.SetLeft(badge, center.X - r);
            Canvas.SetTop(badge, center.Y - r);
            _numbersLayer.Children.Add(badge);
        }
    }

    /// <summary>Radio del círculo en píxeles de la captura: crece un poco en capturas grandes.</summary>
    double NumberRadius => Math.Clamp(Math.Max(_image.PixelWidth, _image.PixelHeight) / 110.0, 11, 22);

    static FrameworkElement NumberBadge(int number, Color color, double r)
    {
        // Número blanco sobre colores oscuros, oscuro sobre claros (amarillo, blanco)
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        var grid = new Grid { Width = 2 * r, Height = 2 * r };
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = Math.Max(1.5, r / 7),
        });
        grid.Children.Add(new TextBlock
        {
            Text = number.ToString(),
            FontSize = r * 1.1,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = luminance > 0.6 ? new SolidColorBrush(Color.FromRgb(27, 27, 31)) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, r * 0.08),
        });
        return grid;
    }
}
