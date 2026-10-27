// ----------------------------------------------------------------------------
//  Editor — ideas por número
//
//  Cada número puede llevar una idea: un texto que no sale en la imagen y que
//  viaja con ella al copiar, para pasársela a una IA. La idea vive en el propio
//  elemento (propiedad del ISF en el trazo, NoteText.Idea en el texto), así se
//  mueve, se borra y se deshace con él. Cada guardado escribe el encabezado y la
//  lista en ideas.txt (Session.IdeasPath): copiar pega su ruta junto a la de la imagen.
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
    // _ideaLayer está en la tabla pero fuera de _surface: nada de lo que tiene (las marcas, el
    // número fantasma) sale en la imagen exportada. Sin fondo: los clics en vacío pasan a la
    // tinta. El campo va en una tarjeta fuera del zoom (EditorView.IdeaCard.cs).
    Canvas _ideaLayer;
    Canvas _ideaMarks;   // marcas de "este número tiene idea"; no reciben clics
    TextBox _ideaBox;    // el campo abierto, o null
    object _ideaTarget;  // su trazo o texto
    bool _ideaPristine;  // se abrió vacío y no se escribió nada: los atajos siguen al editor (IdeaKeys)
    bool _ideaOffered;   // se abrió solo después de un trazo (no lo pidió el usuario): sin número fantasma

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
        if (!IsTiedArrow(s)) OpenIdea(s, offered: true);
    }

    /// <summary>Abre el campo de la idea de <paramref name="target"/> junto a su número, con el foco puesto.</summary>
    /// <param name="offered">Se abre solo después de un trazo: si se cierra vacío no debe dejar rastro, ni siquiera el fantasma.</param>
    void OpenIdea(object target, bool offered = false)
    {
        CommitIdea();
        if (!IsAlive(target)) return;
        _ideaOffered = offered;
        MarkTouched(); // escribiendo: aunque cambie de ventana, no se cierra solo

        _ideaTarget = target;
        _ideaBox = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Text = IdeaOf(target) ?? "",
            ToolTip = "No sale en la imagen: se copia junto a ella",
        };
        _ideaBox.SetResourceReference(ForegroundProperty, "Texto");
        _ideaBox.CaretIndex = _ideaBox.Text.Length;
        _ideaPristine = _ideaBox.Text.Length == 0;
        _ideaBox.TextChanged += (s, e) => _ideaPristine = false;
        _ideaBox.LostKeyboardFocus += (s, e) => { if (s == _ideaBox) CommitIdea(); }; // no uno nuevo por un aviso tardío del viejo
        ShowIdeaCard(_ideaBox, target); // la tarjeta flotante (EditorView.IdeaCard.cs)

        var box = _ideaBox;
        Dispatcher.InvokeAsync(() => { if (_ideaBox == box) box.Focus(); }, DispatcherPriority.Input);
        RefreshGhost();
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
        HideIdeaCard();
        if (hadFocus) Focus(); // las teclas vuelven al editor
        if (IsAlive(target) && ChangeIdea(target, box.Text) && Ideas.HasText(IdeaOf(target)))
            PulseNumber(target); // quedó guardada (EditorView.IdeaCard.cs)
        RefreshGhost();
    }

    /// <summary>Cambia la idea de <paramref name="target"/>, con deshacer. false si quedó igual.</summary>
    bool ChangeIdea(object target, string after)
    {
        after = Ideas.HasText(after) ? after.Trim() : null;
        var before = IdeaOf(target);
        if (before == after) return false;
        SetIdeaRaw(target, after);
        PushUndo(new ActionChange(() => SetIdeaRaw(target, before), () => SetIdeaRaw(target, after)));
        return true;
    }

    // ------------------------------------------------------------------ escribir sobre lo elegido
    // Con un solo trazo o texto elegido a mano, escribir abre su idea con lo tecleado: las letras
    // dejan de cambiar de herramienta hasta soltarlo (Esc). 1–3 siguen siendo el grosor. Lo
    // recién dibujado no cuenta: su campo ya se ofrece solo.

    /// <summary>A qué elemento iría lo que se escriba, o null.</summary>
    object TypingIdeaTarget()
    {
        if (_selectionAuto || _cropMode || _selection.Count + _selectedTexts.Count != 1) return null;
        object target = (object)_selection.FirstOrDefault() ?? _selectedTexts[0];
        return target is Stroke s && IsTiedArrow(s) ? null : target; // la flecha unida no lleva número
    }

    /// <summary>¿La tecla (sin modificadores) escribe en la idea de lo elegido en vez de ser un atajo?</summary>
    bool TypesIdea(Key key) =>
        (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or >= Key.Oem1 and <= Key.Oem102 or Key.Multiply or Key.Add or Key.Subtract
            or Key.Decimal or Key.Divide)
        && key is not (Key.D1 or Key.D2 or Key.D3 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3)
        && TypingIdeaTarget() != null;

    void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_editing != null || e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
        if (_ideaBox != null)
        {
            // Recién abierto, el foco llega al campo un momento después: lo tecleado entretanto, ahí
            if (_ideaBox.IsKeyboardFocusWithin) return;
            AppendToIdea(e.Text);
            e.Handled = true;
            return;
        }
        var target = TypingIdeaTarget();
        if (target == null) return;
        OpenIdea(target);
        if (_ideaBox == null) return;
        AppendToIdea(e.Text);
        e.Handled = true;
    }

    void AppendToIdea(string text)
    {
        _ideaBox.AppendText(text);
        _ideaBox.CaretIndex = _ideaBox.Text.Length;
    }

    /// <summary>Teclas con el campo abierto (OnKeyDown la consulta primero): true = son del campo. Ver IdeaKeys.</summary>
    bool IdeaKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (IdeaKeys.Decide(key, Keyboard.Modifiers, _ideaPristine))
        {
            case IdeaKeyResult.Save:
                CommitIdea();
                e.Handled = true;
                return true;
            case IdeaKeyResult.Escape:
                // Como el Esc del editor después de un trazo: suelta lo seleccionado, sin cerrar la pestaña
                CommitIdea();
                ClearSelection();
                e.Handled = true;
                return true;
            case IdeaKeyResult.PassToEditor:
                CommitIdea();
                return false;
            default:
                return true;
        }
    }

    // ------------------------------------------------------------------ lista en la barra de estado
    Button _ideasButton;

    FrameworkElement BuildIdeasButton()
    {
        _ideasButton = Ui.Btn("", "Las ideas de esta captura: ir a una o copiar el texto", ShowIdeasMenu, style: "EstiloPlano");
        _ideasButton.Margin = new Thickness(0, 0, 8, 0);
        _ideasButton.Visibility = Visibility.Collapsed;
        return _ideasButton;
    }

    /// <summary>«N ideas», oculto sin ninguna. Lo llama RefreshNumbers (en cada cambio).</summary>
    void RefreshIdeasButton()
    {
        if (_ideasButton == null) return;
        int n = Numbering().Count(i => Ideas.HasText(IdeaOf(i.target)));
        _ideasButton.Content = n == 1 ? "1 idea" : $"{n} ideas";
        _ideasButton.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Lo que se copia: el encabezado de Ajustes y la lista. null si no hay ideas.</summary>
    string IdeasText() => Ideas.WithHeader(AppSettings.Current.EncabezadoIdeas, IdeasList());

    void ShowIdeasMenu()
    {
        CommitIdea(); // la idea a medio escribir también entra en la lista
        var menu = new ContextMenu { PlacementTarget = _ideasButton, Placement = PlacementMode.Top };
        foreach (var (target, number) in Numbering())
        {
            var idea = IdeaOf(target);
            if (!Ideas.HasText(idea)) continue;
            var line = idea.Trim().Replace("\r\n", "\n").Split('\n')[0];
            if (line.Length > 60) line = line[..59] + "…";
            // TextBlock y no string: en un string, "_" marcaría una tecla de acceso
            var item = new MenuItem { Header = new TextBlock { Text = $"{number}. {line}" } };
            item.Click += (s, e) => GoToIdea(target);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = "Copiar texto", ToolTip = "Solo el texto de las ideas, con el encabezado de Ajustes" };
        copy.Click += (s, e) => CopyIdeasText();
        menu.Items.Add(copy);
        menu.IsOpen = true;
    }

    /// <summary>Trae el número a la vista y abre su campo.</summary>
    void GoToIdea(object target)
    {
        if (!IsAlive(target)) return;
        var c = AutoNumberCenter(target);
        double r = NumberRadius;
        _board.BringIntoView(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
        // Después de que el menú se cierre y devuelva el foco: si no, el campo lo perdería y se cerraría
        Dispatcher.InvokeAsync(() => OpenIdea(target), DispatcherPriority.Background);
    }

    void CopyIdeasText()
    {
        CommitIdea();
        var text = IdeasText();
        if (text == null) return;
        _status.Text = ClipboardHelper.SetText(text)
            ? "✓ Ideas copiadas al portapapeles"
            : "No se pudo acceder al portapapeles. Intenta de nuevo.";
        MarkTouched(); // como copiar sin cerrar: una captura nueva no se cierra sola al irse a pegarlo
    }

    // ------------------------------------------------------------------ número fantasma
    // Lo que no muestra número (no tiene idea) no tendría dónde hacer
    // clic para anotarlo: al elegirlo aparece un número tenue donde iría, con el que tomaría.
    // Solo en el editor; no cuenta para la lista.
    FrameworkElement _ghost;

    void RefreshGhost()
    {
        if (_ideaLayer == null) return;
        if (_ghost != null)
        {
            _ideaLayer.Children.Remove(_ghost);
            _ghost = null;
        }

        // Un solo elemento elegido a mano; si no, el del campo abierto a pedido. Lo recién dibujado
        // (selección automática, campo ofrecido) no lo muestra: cerrado vacío, no deja rastro.
        object target = !_selectionAuto && _selection.Count + _selectedTexts.Count == 1
            ? (object)_selection.FirstOrDefault() ?? _selectedTexts[0]
            : _ideaOffered ? null : _ideaTarget;
        if (target == null || !IsAlive(target) || target is Stroke s && IsTiedArrow(s)) return;
        var (targets, candidates) = NumberCandidates();
        int index = targets.IndexOf(target);
        if (index < 0 || AssignNumbers(candidates)[index] > 0) return; // ya tiene número (o se numera todo)

        double r = NumberRadius;
        var ghost = NumberBadge(Ideas.GhostNumber(candidates, index), NumberColorOf(target), r);
        ghost.Opacity = 0.45;
        ghost.Cursor = Cursors.Hand;
        ghost.ToolTip = "Clic para escribir su idea · sin idea no lleva número";
        var c = AutoNumberCenter(target);
        Canvas.SetLeft(ghost, c.X - r);
        Canvas.SetTop(ghost, c.Y - r);
        ghost.MouseLeftButtonDown += (sender, e) =>
        {
            e.Handled = true;
            OpenIdea(target);
        };
        _ideaLayer.Children.Add(ghost);
        _ghost = ghost;
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
