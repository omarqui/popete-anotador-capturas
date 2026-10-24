// ----------------------------------------------------------------------------
//  Editor — puntero propio
//
//  El cursor del sistema se oculta sobre la imagen y se dibuja uno en una capa
//  encima del lienzo (fuera de _surface: no sale en la imagen exportada).
//  Estilo "halo y retícula" (propuesta B del canvas): anillo del tamaño real
//  del trazo en el color actual con halo, cuatro marcas de retícula y etiqueta
//  de grosor. Todo lleva contraste claro y oscuro para verse sobre cualquier
//  fondo. El tamaño sigue al grosor y al zoom.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    static readonly Brush PointerLight = Freeze(Brushes.White);
    static readonly Brush PointerDark = Freeze(new SolidColorBrush(Color.FromRgb(17, 17, 17)));

    Canvas _pointerLayer;
    FrameworkElement _pointer;
    Point _pointerHot;      // punto activo dentro de _pointer
    bool _pointerOver;      // el mouse está sobre la imagen

    Canvas BuildPointerLayer()
    {
        _pointerLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _ink.MouseEnter += (s, e) => { _pointerOver = true; MovePointer(e.GetPosition(_pointerLayer)); };
        _ink.MouseLeave += (s, e) => { _pointerOver = false; MovePointer(e.GetPosition(_pointerLayer)); };
        _ink.PreviewMouseMove += (s, e) => MovePointer(e.GetPosition(_pointerLayer));
        return _pointerLayer;
    }

    void MovePointer(Point p)
    {
        if (_pointer == null) return;
        _pointer.Visibility = _pointerOver ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(_pointer, Math.Round(p.X - _pointerHot.X));
        Canvas.SetTop(_pointer, Math.Round(p.Y - _pointerHot.Y));
    }

    /// <summary>Rehace el puntero según herramienta, color, grosor y zoom.</summary>
    void UpdatePointer()
    {
        if (_pointerLayer == null) return;
        _pointerLayer.Children.Clear();
        _pointer = BuildPointer(out _pointerHot);
        _pointerLayer.Children.Add(_pointer);
        MovePointer(Mouse.GetPosition(_pointerLayer));
    }

    FrameworkElement BuildPointer(out Point hot)
    {
        double z = _zoom.ScaleX;
        var color = CurrentColor;
        var c = new Canvas();

        switch (_tool)
        {
            case Tool.Select:
            case Tool.Crop:
                hot = new Point(0, 0); // usan los cursores del sistema
                break;
            case Tool.Pen:
            {
                // Anillo del tamaño real del trazo en el color actual, con halo, retícula y grosor
                double d = Math.Max(10, _size * z), box = d + 28;
                c.Width = c.Height = box;
                hot = new Point(box / 2, box / 2);
                Haloed(c, new EllipseGeometry(hot, d / 2, d / 2), color);
                Ticks(c, hot, d / 2 + 3, d / 2 + 10);
                Outlined(c, new EllipseGeometry(hot, 1, 1), PointerDark, 1);
                SizeLabel(c, hot.X + d / 2 + 4, hot.Y + d / 2 + 2);
                break;
            }
            case Tool.Highlighter:
            {
                // Rectángulo de la punta del resaltador en el color actual, con halo, retícula y grosor
                var a = MakeAttributes();
                double w = Math.Max(6, a.Width * z), h = Math.Max(10, a.Height * z);
                c.Width = w + 28;
                c.Height = h + 28;
                hot = new Point(c.Width / 2, c.Height / 2);
                Haloed(c, new RectangleGeometry(new Rect(hot.X - w / 2, hot.Y - h / 2, w, h), 1.5, 1.5), color);
                Ticks(c, hot, w / 2 + 3, w / 2 + 10, h / 2 + 3, h / 2 + 10);
                SizeLabel(c, hot.X + w / 2 + 4, hot.Y + h / 2 + 2);
                break;
            }
            case Tool.Text:
            {
                // Cursor de texto de la altura de la letra que se va a escribir
                double h = Math.Clamp(FontSizeFor(_size) * z, 14, 96);
                c.Width = 14;
                c.Height = h + 6;
                hot = new Point(7, c.Height / 2);
                var g = new GeometryGroup();
                g.Children.Add(new LineGeometry(new Point(7, 3), new Point(7, h + 3)));
                g.Children.Add(new LineGeometry(new Point(3, 3), new Point(11, 3)));
                g.Children.Add(new LineGeometry(new Point(3, h + 3), new Point(11, h + 3)));
                Outlined(c, g, null);
                break;
            }
            case Tool.Eraser:
            {
                c.Width = c.Height = 24;
                hot = new Point(12, 12);
                Outlined(c, new EllipseGeometry(hot, 8, 8), Freeze(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))));
                var x = new GeometryGroup();
                x.Children.Add(new LineGeometry(new Point(9, 9), new Point(15, 15)));
                x.Children.Add(new LineGeometry(new Point(15, 9), new Point(9, 15)));
                Outlined(c, x, null);
                break;
            }
            default:
            {
                // Línea, flecha y rectángulo: retícula con hueco central y punto del color actual
                c.Width = c.Height = 34;
                hot = new Point(17, 17);
                Ticks(c, hot, 5, 15);
                c.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = new EllipseGeometry(hot, 3, 3),
                    Fill = Freeze(new SolidColorBrush(color)),
                    Stroke = PointerDark,
                    StrokeThickness = 1.25,
                });
                break;
            }
        }
        return c;
    }

    static readonly Brush HaloBrush = Freeze(new SolidColorBrush(Color.FromArgb(200, 17, 17, 17)));

    /// <summary>
    /// Contorno en el color actual con halo: blanco por fuera, oscuro en medio y el color encima.
    /// Se ve sobre fondos claros (por el halo oscuro) y oscuros (por el blanco).
    /// </summary>
    static void Haloed(Canvas c, Geometry g, Color color)
    {
        g.Freeze();
        foreach (var (brush, thickness) in new (Brush, double)[]
        {
            (Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))), 6.5),
            (HaloBrush, 4.5),
            (Freeze(new SolidColorBrush(color)), 2.5),
        })
            c.Children.Add(new System.Windows.Shapes.Path { Data = g, Stroke = brush, StrokeThickness = thickness });
    }

    /// <summary>Cuatro marcas de retícula alrededor de <paramref name="hot"/>: blancas con borde oscuro.</summary>
    static void Ticks(Canvas c, Point hot, double fromX, double toX, double fromY = -1, double toY = -1)
    {
        if (fromY < 0) { fromY = fromX; toY = toX; }
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry(new Point(hot.X, hot.Y - toY), new Point(hot.X, hot.Y - fromY)));
        g.Children.Add(new LineGeometry(new Point(hot.X, hot.Y + fromY), new Point(hot.X, hot.Y + toY)));
        g.Children.Add(new LineGeometry(new Point(hot.X - toX, hot.Y), new Point(hot.X - fromX, hot.Y)));
        g.Children.Add(new LineGeometry(new Point(hot.X + fromX, hot.Y), new Point(hot.X + toX, hot.Y)));
        g.Freeze();
        foreach (var (brush, thickness) in new[] { (HaloBrush, 3.5), (PointerLight, 1.5) })
            c.Children.Add(new System.Windows.Shapes.Path
            {
                Data = g,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
    }

    /// <summary>Etiqueta con el grosor actual ("4 px"), abajo a la derecha del puntero.</summary>
    void SizeLabel(Canvas c, double x, double y)
    {
        var label = new Border
        {
            Background = HaloBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 2),
            Child = new TextBlock
            {
                Text = $"{_size} px",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = PointerLight,
            },
        };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        c.Children.Add(label);
    }

    /// <summary>Agrega una figura con doble contorno: blanco grueso debajo, oscuro fino encima.</summary>
    static void Outlined(Canvas c, Geometry g, Brush fill, double darkThickness = 1.25)
    {
        g.Freeze();
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = g,
            Fill = fill,
            Stroke = PointerLight,
            StrokeThickness = darkThickness + 1.75,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = g,
            Stroke = PointerDark,
            StrokeThickness = darkThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
    }

    static Brush Freeze(Brush b)
    {
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}
