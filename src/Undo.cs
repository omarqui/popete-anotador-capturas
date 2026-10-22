// ----------------------------------------------------------------------------
//  Deshacer / rehacer
//
//  Cada cambio sabe deshacerse y volver a aplicarse. Se ejecutan con
//  _suppress = true para que los eventos de la tinta no los registren de nuevo.
// ----------------------------------------------------------------------------
interface IUndo
{
    void Undo();
    void Redo();
}

sealed class StrokeChange : IUndo
{
    readonly StrokeCollection _target, _added, _removed;
    public StrokeChange(StrokeCollection target, StrokeCollection added, StrokeCollection removed)
    {
        _target = target;
        _added = new StrokeCollection(added);
        _removed = new StrokeCollection(removed);
    }
    public void Undo()
    {
        foreach (var s in _added) if (_target.Contains(s)) _target.Remove(s);
        foreach (var s in _removed) if (!_target.Contains(s)) _target.Add(s);
    }
    public void Redo()
    {
        foreach (var s in _removed) if (_target.Contains(s)) _target.Remove(s);
        foreach (var s in _added) if (!_target.Contains(s)) _target.Add(s);
    }
}

/// <summary>Cambio de color o grosor de trazos ya dibujados.</summary>
sealed class AttributeChange : IUndo
{
    readonly List<(Stroke stroke, DrawingAttributes before, DrawingAttributes after)> _items;
    public AttributeChange(List<(Stroke, DrawingAttributes, DrawingAttributes)> items) { _items = items; }
    public void Undo() { foreach (var (s, before, _) in _items) s.DrawingAttributes = before; }
    public void Redo() { foreach (var (s, _, after) in _items) s.DrawingAttributes = after; }
}

sealed class ElementAdd : IUndo
{
    readonly Panel _panel; readonly UIElement _element;
    public ElementAdd(Panel panel, UIElement element) { _panel = panel; _element = element; }
    public void Undo() => _panel.Children.Remove(_element);
    public void Redo() { if (!_panel.Children.Contains(_element)) _panel.Children.Add(_element); }
}

sealed class ElementRemove : IUndo
{
    readonly Panel _panel; readonly UIElement _element;
    public ElementRemove(Panel panel, UIElement element) { _panel = panel; _element = element; }
    public void Undo() { if (!_panel.Children.Contains(_element)) _panel.Children.Add(_element); }
    public void Redo() => _panel.Children.Remove(_element);
}

/// <summary>Texto corregido con doble clic: contenido, color y tamaño.</summary>
sealed class TextEdit : IUndo
{
    readonly TextBlock _block;
    readonly (string text, Brush color, double size) _before, _after;
    public TextEdit(TextBlock block, (string, Brush, double) before, (string, Brush, double) after)
    { _block = block; _before = before; _after = after; }
    void Apply((string text, Brush color, double size) v)
    {
        _block.Text = v.text;
        _block.Foreground = v.color;
        _block.FontSize = v.size;
    }
    public void Undo() => Apply(_before);
    public void Redo() => Apply(_after);
}

/// <summary>Cambio descrito por dos acciones (por ejemplo, el recorte).</summary>
sealed class ActionChange : IUndo
{
    readonly Action _undo, _redo;
    public ActionChange(Action undo, Action redo) { _undo = undo; _redo = redo; }
    public void Undo() => _undo();
    public void Redo() => _redo();
}

/// <summary>Varios cambios que se deshacen juntos (por ejemplo, sobre una selección múltiple).</summary>
sealed class CompositeChange : IUndo
{
    readonly List<IUndo> _items;
    public CompositeChange(IEnumerable<IUndo> items) { _items = items.ToList(); }
    public void Undo() { for (int i = _items.Count - 1; i >= 0; i--) _items[i].Undo(); }
    public void Redo() { foreach (var item in _items) item.Redo(); }
}

sealed class ClearAllChange : IUndo
{
    readonly StrokeCollection _target, _strokes; readonly Panel _panel; readonly List<UIElement> _elements;
    public ClearAllChange(StrokeCollection target, StrokeCollection strokes, Panel panel, List<UIElement> elements)
    { _target = target; _strokes = strokes; _panel = panel; _elements = elements; }
    public void Undo()
    {
        _target.Add(_strokes);
        foreach (var e in _elements) _panel.Children.Add(e);
    }
    public void Redo()
    {
        _target.Remove(_strokes);
        foreach (var e in _elements) _panel.Children.Remove(e);
    }
}
