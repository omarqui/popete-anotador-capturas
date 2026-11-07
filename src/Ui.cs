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
    public const string Ellipse = "M21 12 A9 6.5 0 1 1 3 12 A9 6.5 0 1 1 21 12 Z";
    public const string Text = "M5 7 V5 H19 V7 M12 5 V19 M9 19 H15";
    public const string TextLabel = "M5 5 H19 A2 2 0 0 1 21 7 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 V7 A2 2 0 0 1 5 5 Z M8 9 H16 M12 9 V15.5";
    public const string Shapes = "M3 12 H11 V20 H3 Z M21 7.5 A4.5 4.5 0 1 1 12 7.5 A4.5 4.5 0 1 1 21 7.5 Z M14 20 L17.5 14 L21 20 Z";
    public const string ChevronDown = "M7 10 L12 15 L17 10";
    public const string ChevronRight = "M10 7 L15 12 L10 17";
    public const string Eraser = "M8 20 H20 M4.5 15.5 L13.5 6.5 L18.5 11.5 L10 20 H8 Z M9 11 L14 16";
    public const string Undo = "M9 14 L4 9 L9 4 M4 9 H14 A6 6 0 0 1 14 21 H11";
    public const string Redo = "M15 14 L20 9 L15 4 M20 9 H10 A6 6 0 0 0 10 21 H13";
    public const string Crop = "M6 2 V18 H22 M2 6 H18 V22";
    /// <summary>Mano que agarra (cursor del pan).</summary>
    public const string Grab = "M18 11.5 V9 A2 2 0 0 0 16 7 A2 2 0 0 0 14 9 V10.4 M14 10 V8 A2 2 0 0 0 12 6 A2 2 0 0 0 10 8 V10 "
        + "M10 9.9 V9 A2 2 0 0 0 8 7 A2 2 0 0 0 6 9 V14 "
        + "M18 11 A2 2 0 0 1 20 9 A2 2 0 0 1 22 11 V14 A8 8 0 0 1 14 22 H10 A8 8 0 0 1 2 14 A2 2 0 0 1 4 12 A2 2 0 0 1 6 14";
    // Tuerca de seis dientes (como el ícono «settings» de Lucide) con el agujero en el centro
    public const string Settings =
        "M12.22 2 H11.78 A2 2 0 0 0 9.78 4 V4.18 A2 2 0 0 1 8.78 5.91 L8.35 6.16 A2 2 0 0 1 6.35 6.16 L6.2 6.08 A2 2 0 0 0 3.47 6.81 "
        + "L3.25 7.19 A2 2 0 0 0 3.98 9.92 L4.13 10.02 A2 2 0 0 1 5.13 11.74 V12.25 A2 2 0 0 1 4.13 13.99 L3.98 14.08 A2 2 0 0 0 3.25 16.81 "
        + "L3.47 17.19 A2 2 0 0 0 6.2 17.92 L6.35 17.84 A2 2 0 0 1 8.35 17.84 L8.78 18.09 A2 2 0 0 1 9.78 19.82 V20 A2 2 0 0 0 11.78 22 "
        + "H12.22 A2 2 0 0 0 14.22 20 V19.82 A2 2 0 0 1 15.22 18.09 L15.65 17.84 A2 2 0 0 1 17.65 17.84 L17.8 17.92 A2 2 0 0 0 20.53 17.19 "
        + "L20.75 16.8 A2 2 0 0 0 20.02 14.07 L19.87 13.99 A2 2 0 0 1 18.87 12.25 V11.75 A2 2 0 0 1 19.87 10.01 L20.02 9.92 A2 2 0 0 0 20.75 7.19 "
        + "L20.53 6.81 A2 2 0 0 0 17.8 6.08 L17.65 6.16 A2 2 0 0 1 15.65 6.16 L15.22 5.91 A2 2 0 0 1 14.22 4.18 V4 A2 2 0 0 0 12.22 2 Z "
        + "M15 12 A3 3 0 1 1 9 12 A3 3 0 1 1 15 12 Z";
    public const string Trash = "M4 7 H20 M10 11 V17 M14 11 V17 M6 7 L7 20 H17 L18 7 M9 7 V4 H15 V7";
    public const string Save = "M12 4 V15 M7 10 L12 15 L17 10 M5 20 H19";
    public const string Copy = "M10 8 H18 A2 2 0 0 1 20 10 V18 A2 2 0 0 1 18 20 H10 A2 2 0 0 1 8 18 V10 A2 2 0 0 1 10 8 Z M16 8 V5 A1 1 0 0 0 15 4 H5 A1 1 0 0 0 4 5 V15 A1 1 0 0 0 5 16 H8";
    public const string Check = "M5 12 L10 17 L19 7";
    public const string Theme = "M20 14.5 A8 8 0 1 1 9.5 4 A6.5 6.5 0 0 0 20 14.5 Z";
    public const string Sun = "M16 12 A4 4 0 1 1 8 12 A4 4 0 1 1 16 12 Z M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9";
    public const string Recents = "M3 12 A9 9 0 1 0 6 5.3 M3 4 V8 H7 M12 7 V12 L15 14";
    public const string Close = "M6 6 L18 18 M18 6 L6 18";
    /// <summary>Tres puntos (los trazos casi nulos con punta redonda se ven como puntos).</summary>
    public const string More = "M6 12 L6.01 12 M12 12 L12.01 12 M18 12 L18.01 12";
    /// <summary>Agarradera: dos columnas de tres puntos.</summary>
    public const string Grip = "M9 6 L9.01 6 M9 12 L9.01 12 M9 18 L9.01 18 M15 6 L15.01 6 M15 12 L15.01 12 M15 18 L15.01 18";
    public const string Refresh = "M20 11 A8 8 0 1 0 18 17 M20 5 V11 H14";
    public const string Info = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z M12 11 V16.5 M12 7.6 V7.8";
    public const string Idea = "M9 18 H15 M10 21 H14 M9 15.5 A6 6 0 1 1 15 15.5 V16 H9 Z";
    public const string Link = "M14 4 H20 V10 M20 4 L11 13 M18 14 V18 A2 2 0 0 1 16 20 H6 A2 2 0 0 1 4 18 V8 A2 2 0 0 1 6 6 H10";
    public const string Folder ="M3 7 A2 2 0 0 1 5 5 H9 L11 7 H19 A2 2 0 0 1 21 9 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z";
}
