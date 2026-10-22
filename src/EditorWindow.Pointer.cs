// ----------------------------------------------------------------------------
//  Editor — puntero propio
//
//  El cursor del sistema se oculta sobre la imagen y se dibuja uno en una capa
//  encima del lienzo (fuera de _surface: no sale en la imagen exportada).
//  Todas las formas llevan doble contorno, blanco debajo y casi negro encima,
//  para que se vean sobre cualquier fondo. El tamaño sigue al grosor y al zoom.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
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
        var tint = Freeze(new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)));
        var c = new Canvas();

        switch (_tool)
        {
            case Tool.Pen:
            {
                // Anillo del tamaño real del trazo, relleno con el color actual
                double d = Math.Max(6, _size * z), box = d + 6;
                c.Width = c.Height = box;
                hot = new Point(box / 2, box / 2);
                Outlined(c, new EllipseGeometry(hot, d / 2, d / 2), tint);
                Outlined(c, new EllipseGeometry(hot, 1, 1), PointerDark, 1);
                break;
            }
            case Tool.Highlighter:
            {
                // Rectángulo de la punta del resaltador
                var a = MakeAttributes();
                double w = Math.Max(4, a.Width * z), h = Math.Max(8, a.Height * z);
                c.Width = w + 6;
                c.Height = h + 6;
                hot = new Point(c.Width / 2, c.Height / 2);
                Outlined(c, new RectangleGeometry(new Rect(3, 3, w, h), 1.5, 1.5), tint);
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
                c.Width = c.Height = 26;
                hot = new Point(13, 13);
                var g = new GeometryGroup();
                g.Children.Add(new LineGeometry(new Point(13, 1), new Point(13, 8)));
                g.Children.Add(new LineGeometry(new Point(13, 18), new Point(13, 25)));
                g.Children.Add(new LineGeometry(new Point(1, 13), new Point(8, 13)));
                g.Children.Add(new LineGeometry(new Point(18, 13), new Point(25, 13)));
                Outlined(c, g, null);
                c.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = new EllipseGeometry(hot, 2.5, 2.5),
                    Fill = Freeze(new SolidColorBrush(color)),
                    Stroke = PointerLight,
                    StrokeThickness = 1,
                });
                break;
            }
        }
        return c;
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
