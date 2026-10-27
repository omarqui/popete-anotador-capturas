// ----------------------------------------------------------------------------
//  Editor — ideas por número
//
//  Cada número puede llevar una idea: un texto que no sale en la imagen y que
//  viaja con ella al copiar, para pasársela a una IA. La idea vive en el propio
//  elemento (propiedad del ISF en el trazo, NoteText.Idea en el texto), así se
//  mueve, se borra y se deshace con él. Cada guardado escribe la lista en
//  ideas.txt (Session.IdeasPath), que es lo que lee el historial rápido.
//  Qué número lleva cada cosa y el formato del texto están en Ideas.cs.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Idea del número del trazo (propiedad del ISF: string).</summary>
    static readonly Guid IdeaProperty = new Guid("e4b7c2a9-3d1f-4a6e-8b5c-9f0d2e7a1c46");

    /// <summary>
    /// Flecha libre (propiedad del ISF): no empezó sobre otro trazo y se numera como cualquiera.
    /// Se marcan las libres y no las unidas para que las flechas de capturas viejas, que no
    /// tienen el dato, sigan sin número.
    /// </summary>
    static readonly Guid FreeArrowProperty = new Guid("1a9e5d3c-7b2f-4c8a-b6e4-0d3f8a2c5e71");

    /// <summary>Flecha que sale de otro objeto: nunca lleva número; su idea va en el objeto.</summary>
    static bool IsTiedArrow(Stroke s) => IsArrow(s) && !s.ContainsPropertyData(FreeArrowProperty);

    static string IdeaOf(object target) => target switch
    {
        Stroke s when s.ContainsPropertyData(IdeaProperty) => s.GetPropertyData(IdeaProperty) as string,
        NoteText nt => nt.Idea,
        _ => null,
    };

    /// <summary>Pone la idea sin deshacer; vacía o de solo espacios, la quita.</summary>
    static void SetIdeaRaw(object target, string idea)
    {
        bool has = Ideas.HasText(idea);
        switch (target)
        {
            case Stroke s when has: s.AddPropertyData(IdeaProperty, idea); break;
            case Stroke s: if (s.ContainsPropertyData(IdeaProperty)) s.RemovePropertyData(IdeaProperty); break;
            case NoteText nt: nt.Idea = has ? idea : null; break;
        }
    }

    /// <summary>«1. idea», en el orden de los números; "" si no hay ninguna.</summary>
    string IdeasList() => Ideas.List(Numbering().Select(n => (n.number, IdeaOf(n.target))));

    // ------------------------------------------------------------------ capa y campo
    // _ideaLayer está en la tabla pero fuera de _surface: nada de lo que tiene (el campo, las
    // marcas, el número fantasma) sale en la imagen exportada. Sin fondo: los clics en vacío
    // pasan a la tinta.
    Canvas _ideaLayer;
    Canvas _ideaMarks;   // marcas de "este número tiene idea"; no reciben clics
    TextBox _ideaBox;    // el campo abierto, o null
    object _ideaTarget;  // su trazo o texto

    Canvas BuildIdeaLayer()
    {
        _ideaMarks = new Canvas { IsHitTestVisible = false };
        _ideaLayer = new Canvas();
        _ideaLayer.Children.Add(_ideaMarks);
        return _ideaLayer;
    }

    bool IsAlive(object target) => target switch
    {
        Stroke s => _ink.Strokes.Contains(s),
        TextBlock tb => _textLayer.Children.Contains(tb),
        _ => false,
    };

    /// <summary>Después de un trazo: se ofrece escribir su idea (las flechas unidas no llevan).</summary>
    void OfferIdea(Stroke s)
    {
        if (!IsTiedArrow(s)) OpenIdea(s);
    }

    /// <summary>Abre el campo de la idea de <paramref name="target"/> junto a su número, con el foco puesto.</summary>
    void OpenIdea(object target)
    {
        CommitIdea();
        if (!IsAlive(target)) return;
        MarkTouched(); // escribiendo: aunque cambie de ventana, no se cierra solo

        // Mide lo mismo en pantalla a cualquier zoom (el campo está dentro del zoom)
        double z = _zoom.ScaleX;
        _ideaTarget = target;
        _ideaBox = new TextBox
        {
            MinWidth = 200,
            MaxWidth = 360,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            FontSize = 13,
            Padding = new Thickness(4, 2, 4, 2),
            BorderThickness = new Thickness(1.5),
            LayoutTransform = new ScaleTransform(1 / z, 1 / z),
            Text = IdeaOf(target) ?? "",
            ToolTip = "Idea de este número: no sale en la imagen, se copia junto a ella · Enter guarda · Shift+Enter, otra línea",
        };
        _ideaBox.SetResourceReference(BackgroundProperty, "Superficie");
        _ideaBox.SetResourceReference(ForegroundProperty, "Texto");
        _ideaBox.SetResourceReference(BorderBrushProperty, "Acento");
        _ideaBox.CaretIndex = _ideaBox.Text.Length;

        // A la derecha del número, sin taparlo
        var c = AutoNumberCenter(target);
        double r = NumberRadius;
        Canvas.SetLeft(_ideaBox, c.X + r + 6 / z);
        Canvas.SetTop(_ideaBox, c.Y - r);
        _ideaBox.LostKeyboardFocus += (s, e) => CommitIdea();
        _ideaLayer.Children.Add(_ideaBox);

        var box = _ideaBox;
        Dispatcher.InvokeAsync(() => { if (_ideaBox == box) box.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Cierra el campo y guarda lo escrito (con deshacer). Vacío y sin idea antes: no deja rastro.</summary>
    void CommitIdea()
    {
        if (_ideaBox == null) return;
        var box = _ideaBox;
        var target = _ideaTarget;
        _ideaBox = null; // primero, para que LostKeyboardFocus no reentre
        _ideaTarget = null;
        bool hadFocus = box.IsKeyboardFocusWithin;
        _ideaLayer.Children.Remove(box);
        if (hadFocus) Focus(); // las teclas vuelven al editor
        if (IsAlive(target)) ChangeIdea(target, box.Text);
    }

    /// <summary>Cambia la idea de <paramref name="target"/>, con deshacer.</summary>
    void ChangeIdea(object target, string after)
    {
        after = Ideas.HasText(after) ? after.Trim() : null;
        var before = IdeaOf(target);
        if (before == after) return;
        SetIdeaRaw(target, after);
        PushUndo(new ActionChange(() => SetIdeaRaw(target, before), () => SetIdeaRaw(target, after)));
    }

    /// <summary>
    /// Teclas con el campo abierto (OnKeyDown la consulta primero): true = son del campo.
    /// Enter guarda, Esc cierra guardando. Con el campo vacío, los atajos sin letra (Tab, Supr,
    /// flechas, Ctrl+…, Alt) lo cierran y siguen al editor; las letras y los dígitos escriben.
    /// </summary>
    bool IdeaKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if ((key == Key.Enter && mods == ModifierKeys.None) || key == Key.Escape)
        {
            CommitIdea();
            e.Handled = true;
            return true;
        }
        bool shortcut = key is Key.Tab or Key.Delete or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down
            || mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt);
        if (_ideaBox.Text.Length == 0 && shortcut)
        {
            CommitIdea();
            return false;
        }
        return true;
    }

    /// <summary>La marca del número de un elemento con idea (solo en el editor), sobre su borde superior derecho.</summary>
    void AddIdeaMark(Point center, double r)
    {
        double dot = r * 0.32;
        var mark = new System.Windows.Shapes.Ellipse
        {
            Width = 2 * dot,
            Height = 2 * dot,
            Stroke = Brushes.White,
            StrokeThickness = Math.Max(1, r / 10),
        };
        mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Acento");
        Canvas.SetLeft(mark, center.X + r * 0.72 - dot);
        Canvas.SetTop(mark, center.Y - r * 0.72 - dot);
        _ideaMarks.Children.Add(mark);
    }
}
