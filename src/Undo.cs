// ----------------------------------------------------------------------------
//  Deshacer
// ----------------------------------------------------------------------------
interface IUndo { void Undo(); }

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
}

sealed class ElementAdd : IUndo
{
    readonly Panel _panel; readonly UIElement _element;
    public ElementAdd(Panel panel, UIElement element) { _panel = panel; _element = element; }
    public void Undo() => _panel.Children.Remove(_element);
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
}
