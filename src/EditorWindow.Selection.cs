// ----------------------------------------------------------------------------
//  Editor — selección de trazos
//
//  El trazo o la forma recién dibujados quedan seleccionados: elegir un color
//  o un grosor (clic o teclas 1–4) se aplica a la selección, y Supr la borra.
//  Con la herramienta Seleccionar (V) se elige cualquier trazo con un clic.
//  Tocar Alt o clic derecho NO cambia la selección: sirven para dibujar el
//  siguiente en el otro color. Esc quita la selección (y con nada seleccionado cierra).
//
//  El recuadro de selección va en _selectionLayer, fuera de _surface: no sale
//  en la imagen exportada.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    StrokeCollection _selection = new StrokeCollection();
    Canvas _selectionLayer;

    Canvas BuildSelectionLayer(double width, double height)
    {
        _selectionLayer = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        // Si un trazo seleccionado desaparece (deshacer, borrador, limpiar), sale de la selección
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (e.Removed.Any(_selection.Contains))
                SelectStrokes(new StrokeCollection(_selection.Where(x => !e.Removed.Contains(x))));
        };
        return _selectionLayer;
    }

    bool HasSelection => _selection.Count > 0;

    void SelectStrokes(StrokeCollection strokes)
    {
        _selection = strokes ?? new StrokeCollection();
        UpdateSelectionVisual();
    }

    void ClearSelection()
    {
        if (HasSelection) SelectStrokes(null);
    }

    /// <summary>Recuadro punteado alrededor de la selección; grosor constante en pantalla.</summary>
    void UpdateSelectionVisual()
    {
        if (_selectionLayer == null) return;
        _selectionLayer.Children.Clear();
        if (!HasSelection) return;

        double z = _zoom.ScaleX;
        var b = _selection.GetBounds();
        b.Inflate(6 / z, 6 / z);
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = b.Width,
            Height = b.Height,
            RadiusX = 4 / z,
            RadiusY = 4 / z,
            StrokeThickness = 1.5 / z,
            StrokeDashArray = new DoubleCollection { 4, 3 },
        };
        rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Acento");
        Canvas.SetLeft(rect, b.X);
        Canvas.SetTop(rect, b.Y);
        _selectionLayer.Children.Add(rect);
    }

    /// <summary>Cambia los atributos de los trazos seleccionados, con deshacer.</summary>
    void ApplyToSelection(Action<DrawingAttributes> change)
    {
        if (!HasSelection) return;
        var before = new List<(Stroke, DrawingAttributes)>();
        foreach (var s in _selection)
        {
            before.Add((s, s.DrawingAttributes.Clone()));
            var da = s.DrawingAttributes.Clone();
            change(da);
            s.DrawingAttributes = da;
        }
        PushUndo(new AttributeChange(before));
        UpdateSelectionVisual(); // el grosor cambia el recuadro
    }

    void ApplyColorToSelection(Color color) => ApplyToSelection(da => da.Color = color);

    void ApplySizeToSelection(double size) => ApplyToSelection(da =>
    {
        // Mismas proporciones que MakeAttributes
        da.Width = da.IsHighlighter ? Math.Max(4, size * 1.5) : size;
        da.Height = da.IsHighlighter ? Math.Max(14, size * 4) : size;
    });

    void DeleteSelection()
    {
        if (!HasSelection) return;
        var victims = _selection;
        ClearSelection();
        _ink.Strokes.Remove(victims); // StrokesChanged registra el deshacer
    }

    /// <summary>Herramienta Seleccionar: el trazo de más arriba bajo el clic, o nada.</summary>
    void SelectAt(Point p)
    {
        var hits = _ink.Strokes.HitTest(p, Math.Max(6, 10 / _zoom.ScaleX));
        SelectStrokes(hits.Count > 0 ? new StrokeCollection { hits[hits.Count - 1] } : null);
        if (HasSelection) ReflectSelectionInToolbar();
    }

    /// <summary>Muestra en la barra el color y el grosor del trazo elegido, sin modificarlo.</summary>
    void ReflectSelectionInToolbar()
    {
        var da = _selection[_selection.Count - 1].DrawingAttributes;
        if (da.IsHighlighter) _highlightColor = da.Color; else _penColor = da.Color;
        _size = da.IsHighlighter ? Math.Round(da.Height / 4) : da.Width;
        RefreshSwatches();
        RefreshSizes();
    }
}
