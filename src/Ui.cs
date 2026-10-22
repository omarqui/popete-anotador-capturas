// ----------------------------------------------------------------------------
//  Utilidades de UI
// ----------------------------------------------------------------------------
static class Ui
{
    /// <summary>Botón con texto y, opcionalmente, ícono. Estilo por defecto: "EstiloSecundario".</summary>
    public static Button Btn(string text, string tip, Action action, string icon = null, string style = "EstiloSecundario")
    {
        var b = new Button { Content = Label(text, icon), ToolTip = tip };
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += (s, e) => action();
        return b;
    }

    /// <summary>Botón solo de ícono; el texto va en el tooltip y para lectores de pantalla.</summary>
    public static Button IconBtn(string icon, string tip, Action action)
    {
        var b = new Button { Content = Icon(icon), ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.SetResourceReference(FrameworkElement.StyleProperty, "EstiloIcono");
        b.Click += (s, e) => action();
        return b;
    }

    public static object Label(string text, string icon)
    {
        if (icon == null) return text;
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(Icon(icon, 16));
        p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    /// <summary>
    /// Ícono de trazo en una grilla de 24×24 (datos de <see cref="Icons"/>).
    /// Toma el color del texto del control que lo contiene.
    /// </summary>
    public static FrameworkElement Icon(string data, double size = 20, double thickness = 1.75)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding
        {
            Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
            RelativeSource = System.Windows.Data.RelativeSource.Self,
        });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    public static Border Divider()
    {
        var d = new Border { Width = 1, Height = 28, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        d.SetResourceReference(Border.BackgroundProperty, "BordeFuerte");
        return d;
    }

    /// <summary>Grupo segmentado: superficie con borde que contiene botones.</summary>
    public static Border Group(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        var g = new Border
        {
            Child = p,
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
        };
        g.SetResourceReference(Border.BackgroundProperty, "Superficie");
        g.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return g;
    }

    /// <summary>Tecla dibujada como "keycap", para los atajos de la barra de estado.</summary>
    public static Border Keycap(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Texto");
        var k = new Border
        {
            Child = t,
            Padding = new Thickness(6, 0, 6, 1),
            BorderThickness = new Thickness(1, 1, 1, 2),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        k.SetResourceReference(Border.BackgroundProperty, "Superficie");
        k.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");
        return k;
    }
}

/// <summary>Íconos de trazo (sintaxis de Path de WPF, grilla de 24×24).</summary>
static class Icons
{
    public const string Select = "M6 3 L6 18 L10 14 L13 20.5 L15.5 19.5 L12.5 13 L18 13 Z";
    public const string Pen = "M4 20 L8 19 L19 8 L16 5 L5 16 Z M14 7 L17 10";
    public const string Highlighter = "M9 14 L15 4 L19 7 L12 16 Z M9 14 L8 17 L11 18 L12 16 M4 21 H12";
    public const string Line = "M5 19 L19 5";
    public const string Arrow = "M5 19 L19 5 M10 5 H19 V14";
    public const string Rectangle = "M4 6 H20 V18 H4 Z";
    public const string Text = "M5 7 V5 H19 V7 M12 5 V19 M9 19 H15";
    public const string TextLabel = "M5 5 H19 A2 2 0 0 1 21 7 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 V7 A2 2 0 0 1 5 5 Z M8 9 H16 M12 9 V15.5";
    public const string Shapes = "M3 12 H11 V20 H3 Z M21 7.5 A4.5 4.5 0 1 1 12 7.5 A4.5 4.5 0 1 1 21 7.5 Z M14 20 L17.5 14 L21 20 Z";
    public const string ChevronDown = "M7 10 L12 15 L17 10";
    public const string Eraser = "M8 20 H20 M4.5 15.5 L13.5 6.5 L18.5 11.5 L10 20 H8 Z M9 11 L14 16";
    public const string Undo = "M9 14 L4 9 L9 4 M4 9 H14 A6 6 0 0 1 14 21 H11";
    public const string Redo = "M15 14 L20 9 L15 4 M20 9 H10 A6 6 0 0 0 10 21 H13";
    public const string Crop = "M6 2 V18 H22 M2 6 H18 V22";
    public const string Settings = "M4 6 H13 M17 6 H20 M15 4 V8 M4 12 H7 M11 12 H20 M9 10 V14 M4 18 H11 M15 18 H20 M13 16 V20";
    public const string Trash = "M4 7 H20 M10 11 V17 M14 11 V17 M6 7 L7 20 H17 L18 7 M9 7 V4 H15 V7";
    public const string Save = "M12 4 V15 M7 10 L12 15 L17 10 M5 20 H19";
    public const string Copy = "M10 8 H18 A2 2 0 0 1 20 10 V18 A2 2 0 0 1 18 20 H10 A2 2 0 0 1 8 18 V10 A2 2 0 0 1 10 8 Z M16 8 V5 A1 1 0 0 0 15 4 H5 A1 1 0 0 0 4 5 V15 A1 1 0 0 0 5 16 H8";
    public const string Check = "M5 12 L10 17 L19 7";
    public const string Theme = "M20 14.5 A8 8 0 1 1 9.5 4 A6.5 6.5 0 0 0 20 14.5 Z";
    public const string Sun = "M16 12 A4 4 0 1 1 8 12 A4 4 0 1 1 16 12 Z M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9";
    public const string Recents = "M3 12 A9 9 0 1 0 6 5.3 M3 4 V8 H7 M12 7 V12 L15 14";
    public const string Close = "M6 6 L18 18 M18 6 L6 18";
    public const string Refresh = "M20 11 A8 8 0 1 0 18 17 M20 5 V11 H14";
    public const string Folder = "M3 7 A2 2 0 0 1 5 5 H9 L11 7 H19 A2 2 0 0 1 21 9 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z";
}
