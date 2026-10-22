// ----------------------------------------------------------------------------
//  Editor — formas (línea, flecha, rectángulo) y textos
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    // ------------------------------------------------------------------ formas (línea, flecha, rectángulo) y texto
    void Ink_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Text)
        {
            CommitText();
            BeginText(e.GetPosition(_textLayer));
            e.Handled = true;
            return;
        }
        if (_tool is not (Tool.Line or Tool.Arrow or Tool.Rectangle)) return;

        _start = e.GetPosition(_ink);
        _preview = new Stroke(BuildPoints(_start, _start), MakeAttributes());
        _suppress = true;
        _ink.Strokes.Add(_preview);
        _suppress = false;
        _ink.CaptureMouse();
        e.Handled = true;
    }

    void Ink_MouseMove(object sender, MouseEventArgs e)
    {
        if (_preview == null) return;
        var p = e.GetPosition(_ink);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) p = Constrain(_start, p);
        _preview.StylusPoints = BuildPoints(_start, p);
    }

    void Ink_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_preview == null) return;
        _ink.ReleaseMouseCapture();
        var stroke = _preview;
        _preview = null;

        if ((e.GetPosition(_ink) - _start).Length < 3)
        {
            // Un clic sin arrastrar: se descarta
            _suppress = true;
            _ink.Strokes.Remove(stroke);
            _suppress = false;
        }
        else
        {
            PushUndo(new StrokeChange(_ink.Strokes, new StrokeCollection { stroke }, new StrokeCollection()));
        }
        e.Handled = true;
    }

    StylusPointCollection BuildPoints(Point a, Point b)
    {
        var pts = new List<Point>();
        switch (_tool)
        {
            case Tool.Rectangle:
                pts.AddRange(new[] { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a });
                break;
            case Tool.Arrow:
                var v = b - a;
                double len = v.Length;
                if (len < 1) { pts.Add(a); pts.Add(b); break; }
                v /= len;
                double head = Math.Max(14, _size * 4);
                var n = new Vector(-v.Y, v.X);
                var w1 = b - v * head + n * head * 0.5;
                var w2 = b - v * head - n * head * 0.5;
                pts.AddRange(new[] { a, b, w1, b, w2 });
                break;
            default: // línea
                pts.Add(a);
                pts.Add(b);
                break;
        }
        return new StylusPointCollection(pts);
    }

    Point Constrain(Point a, Point b)
    {
        var v = b - a;
        if (_tool == Tool.Rectangle)
        {
            double m = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            return new Point(a.X + Math.Sign(v.X) * m, a.Y + Math.Sign(v.Y) * m);
        }
        double step = Math.PI / 4;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
        return a + new Vector(Math.Cos(ang), Math.Sin(ang)) * v.Length;
    }

    void BeginText(Point p)
    {
        _editing = new TextBox
        {
            MinWidth = 80,
            FontSize = FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_penColor),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
        };
        _editing.SetResourceReference(BorderBrushProperty, "Acento");
        Canvas.SetLeft(_editing, p.X);
        Canvas.SetTop(_editing, p.Y);
        _textLayer.IsHitTestVisible = true;
        _textLayer.Children.Add(_editing);

        _editing.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { CommitText(); e.Handled = true; }
        };
        _editing.LostKeyboardFocus += (s, e) => CommitText();
        Dispatcher.InvokeAsync(() => { _editing?.Focus(); }, DispatcherPriority.Input);
    }

    void CommitText()
    {
        if (_editing == null) return;
        var tb = _editing;
        _editing = null;                 // primero, para que LostKeyboardFocus no reentre
        _textLayer.Children.Remove(tb);
        _textLayer.IsHitTestVisible = false;
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var block = AddTextBlock(new TextNote
        {
            Text = tb.Text,
            X = Canvas.GetLeft(tb),
            Y = Canvas.GetTop(tb),
            FontSize = tb.FontSize,
            Color = ((SolidColorBrush)tb.Foreground).Color.ToString(),
        });
        PushUndo(new ElementAdd(_textLayer, block));
    }

    TextBlock AddTextBlock(TextNote n)
    {
        var block = new TextBlock
        {
            Text = n.Text,
            FontSize = n.FontSize > 0 ? n.FontSize : FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ParseColor(n.Color)),
            Padding = new Thickness(3, 1, 3, 1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(block, n.X);
        Canvas.SetTop(block, n.Y);
        _textLayer.Children.Add(block);
        return block;
    }

    static Color ParseColor(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return Palette[0].color; }
    }
}
