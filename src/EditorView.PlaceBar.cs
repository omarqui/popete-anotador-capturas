// ----------------------------------------------------------------------------
//  Editor — la barra flotante de la captura en el lugar
//
//  Una sola fila, más corta que la de la pestaña: agarradera, herramientas (el
//  mismo grupo, prestado), color y grosor contraídos, deshacer, «N ideas» (si
//  hay), «⋯» con lo de poco uso, Copiar (copia y cierra) y × (cierra sin copiar,
//  como Esc). Color y grosor muestran solo el actual; un clic despliega los demás
//  en la misma fila, y se contraen al elegir o al empezar a dibujar. Los avisos
//  (_status, prestado) salen en una línea debajo solo mientras dicen algo.
//
//  Mientras se dibuja, la barra se atenúa para no tapar lo que se marca. Se
//  puede arrastrar desde la agarradera; movida, se queda ahí (dentro del área de
//  trabajo) hasta que se cierra la captura.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Opacidad de la barra mientras se dibuja.</summary>
    const double DimmedBar = 0.3;

    Border _floatingBar;
    StackPanel _placeRow;
    Border _placeToolsSlot, _placeIdeasSlot, _placeMessage;
    ToggleButton _placeColorButton, _placeSizeButton;
    FrameworkElement _placeColors, _placeSizes;
    System.Windows.Shapes.Ellipse _placeColorDot;
    System.Windows.Shapes.Ellipse[] _placeColorMinis, _placeSizeDots;

    // Lo que la barra le pide prestado a la de la pestaña, para devolverlo en su lugar
    readonly List<(FrameworkElement element, Panel parent, int index, Thickness margin)> _borrowed =
        new List<(FrameworkElement, Panel, int, Thickness)>();

    bool _barDimmed;

    // Arrastre
    bool _barMoved;
    Point _barWanted, _barGrab;

    /// <summary>Arma la barra (una vez) y le pone lo prestado: herramientas, «N ideas» y los avisos.</summary>
    void MountPlaceBar()
    {
        if (_floatingBar == null) BuildPlaceBar();
        Borrow((FrameworkElement)_toolPanel.Parent, _placeToolsSlot);
        Borrow(_ideasButton, _placeIdeasSlot);
        Borrow(_status, _placeMessage, new Thickness(0));
        CollapsePickers();
        _barMoved = false;
    }

    /// <summary>Devuelve lo prestado a la barra de la pestaña (al pasar la captura a una pestaña).</summary>
    void UnmountPlaceBar()
    {
        foreach (var (element, parent, index, margin) in Enumerable.Reverse(_borrowed))
        {
            ((Border)element.Parent).Child = null;
            element.Margin = margin;
            parent.Children.Insert(Math.Min(index, parent.Children.Count), element);
        }
        _borrowed.Clear();
        CollapsePickers();
        DimPlaceBar(false);
    }

    void Borrow(FrameworkElement element, Border slot, Thickness? margin = null)
    {
        var parent = (Panel)element.Parent;
        _borrowed.Add((element, parent, parent.Children.IndexOf(element), element.Margin));
        parent.Children.Remove(element);
        if (margin is Thickness m) element.Margin = m;
        slot.Child = element;
    }

    void BuildPlaceBar()
    {
        // Una fila de ~44 DIP: entra en la franja (48) arriba de la captura
        _placeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 6, 3) };

        _placeRow.Children.Add(BuildGrip());
        // El grupo de la pestaña mide 42 DIP de alto: aquí, un poco más chico (la escala queda en el hueco, no en el grupo)
        _placeToolsSlot = new Border { VerticalAlignment = VerticalAlignment.Center, LayoutTransform = new ScaleTransform(0.86, 0.86) };
        _placeRow.Children.Add(_placeToolsSlot);
        _placeRow.Children.Add(PlaceDivider());

        // Color: el actual y, chicos y apilados, otros tres de la paleta; un clic los despliega en su lugar
        _placeColorDot = new System.Windows.Shapes.Ellipse { Width = 18, Height = 18, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
        _placeColorDot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "BordeFuerte");
        var minis = new Canvas { Width = 19, Height = 9, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _placeColorMinis = new System.Windows.Shapes.Ellipse[3];
        for (int i = 0; i < _placeColorMinis.Length; i++)
        {
            var mini = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, StrokeThickness = 1.5 };
            mini.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Fondo"); // separa las fichas apiladas
            Canvas.SetLeft(mini, i * 5);
            minis.Children.Add(mini);
            _placeColorMinis[i] = mini;
        }
        var colorPreview = new StackPanel { Orientation = Orientation.Horizontal };
        colorPreview.Children.Add(_placeColorDot);
        colorPreview.Children.Add(minis);
        _placeColorButton = PickerButton(colorPreview);
        _placeColorButton.Click += (s, e) => TogglePicker(colors: true);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (c, name) in Palette)
        {
            var sw = new ToggleButton { Background = new SolidColorBrush(c), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(sw, name);
            sw.SetResourceReference(StyleProperty, "EstiloMuestra");
            var color = c;
            sw.Click += (s, e) => { SetColor(color); CollapsePickers(); };
            _swatches.Add((sw, c)); // RefreshSwatches marca también estas
            colors.Children.Add(sw);
        }
        _placeColors = colors;
        _placeRow.Children.Add(_placeColorButton);
        _placeRow.Children.Add(_placeColors);

        // Grosor: los tres en miniatura, el actual marcado y los otros tenues; un clic los despliega
        var sizePreview = new StackPanel { Orientation = Orientation.Horizontal };
        _placeSizeDots = new System.Windows.Shapes.Ellipse[SizePresets.Length];
        for (int i = 0; i < SizePresets.Length; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = DotFor(SizePresets[i]),
                Height = DotFor(SizePresets[i]),
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding
            {
                Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
                RelativeSource = System.Windows.Data.RelativeSource.Self,
            });
            sizePreview.Children.Add(dot);
            _placeSizeDots[i] = dot;
        }
        _placeSizeButton = PickerButton(sizePreview);
        _placeSizeButton.Margin = new Thickness(4, 0, 0, 0);
        _placeSizeButton.Click += (s, e) => TogglePicker(colors: false);
        var sizes = new List<UIElement>();
        string[] names = { "Fino", "Medio", "Grueso" };
        for (int i = 0; i < SizePresets.Length; i++)
        {
            double size = SizePresets[i];
            var dot = new System.Windows.Shapes.Ellipse { Width = DotFor(size), Height = DotFor(size) };
            dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding
            {
                Path = new PropertyPath(System.Windows.Documents.TextElement.ForegroundProperty),
                RelativeSource = System.Windows.Data.RelativeSource.Self,
            });
            var tip = $"{names[i]}: {size} px ({i + 1})";
            var b = new ToggleButton { Content = dot, ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.SetResourceReference(StyleProperty, "EstiloSegmento");
            b.Click += (s, e) => { SetSize(size); CollapsePickers(); };
            _sizeButtons.Add((b, size)); // RefreshSizes marca también estos
            sizes.Add(b);
        }
        _placeSizes = Ui.Group(sizes.ToArray());
        _placeSizes.Margin = new Thickness(2, 0, 0, 0);
        _placeRow.Children.Add(_placeSizeButton);
        _placeRow.Children.Add(_placeSizes);
        RefreshSwatches();
        RefreshSizes();

        _placeRow.Children.Add(PlaceDivider());
        _placeRow.Children.Add(PlaceIconBtn(Icons.Undo, "Deshacer (Ctrl+Z)", Undo));
        _placeRow.Children.Add(PlaceIconBtn(Icons.Redo, "Rehacer (Ctrl+Y)", Redo));
        _placeRow.Children.Add(PlaceDivider());
        _placeRow.Children.Add(_placeIdeasSlot = new Border { VerticalAlignment = VerticalAlignment.Center });

        // Solo con zoom: dice cuánto y devuelve la captura a su lugar (EditorView.PlaceZoom.cs)
        _placeZoomButton = Ui.Btn("100 %", "Volver al tamaño real, en su lugar (Ctrl+0)", FitZoom, style: "EstiloPlano");
        _placeZoomButton.Margin = new Thickness(0, 0, 4, 0);
        _placeZoomButton.Visibility = Visibility.Collapsed;
        _placeRow.Children.Add(_placeZoomButton);

        var more = PlaceIconBtn(Icons.More, "Más opciones", null);
        more.Click += (s, e) => ShowPlaceMenu(more);
        _placeRow.Children.Add(more);

        // Copia y cierra siempre (como el clic derecho y el clic fuera); Ctrl+C sigue el ajuste
        var copy = Ui.Btn("Copiar", "Copiar y cerrar (Ctrl+Enter, clic derecho en la captura o clic fuera)",
            () => CopyAndMaybeClose(), Icons.Copy, "EstiloPrimario");
        copy.Margin = new Thickness(4, 0, 2, 0);
        _placeRow.Children.Add(copy);
        _placeRow.Children.Add(PlaceIconBtn(Icons.Close, "Cerrar sin copiar (Esc)", Dismiss));

        // Avisos: solo mientras _status dice algo, del ancho de la fila (así el texto se recorta)
        _placeMessage = new Border { Padding = new Thickness(12, 4, 12, 5), BorderThickness = new Thickness(0, 1, 0, 0) };
        _placeMessage.SetResourceReference(Border.BorderBrushProperty, "Borde");
        _placeMessage.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = _placeRow });
        var hideEmpty = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = _status }, Value = "" };
        hideEmpty.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        var messageStyle = new Style(typeof(Border));
        messageStyle.Triggers.Add(hideEmpty);
        _placeMessage.Style = messageStyle;

        var bars = new StackPanel();
        bars.Children.Add(_placeRow);
        bars.Children.Add(_placeMessage);
        _floatingBar = new Border
        {
            Child = bars,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.3 },
        };
        _floatingBar.SetResourceReference(Border.BackgroundProperty, "Fondo");
        _floatingBar.SetResourceReference(Border.BorderBrushProperty, "BordeFuerte");
        _floatingBar.SizeChanged += (s, e) => PlaceFloatingBar();

        // Atenuar y contraer al empezar a dibujar; volver al soltar
        _canvasArea.PreviewMouseLeftButtonDown += (s, e) =>
        {
            if (!_inPlace) return;
            CollapsePickers();
            DimPlaceBar(true);
        };
        PreviewMouseLeftButtonUp += (s, e) => DimPlaceBar(false);
        AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((s, e) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) DimPlaceBar(false);
        }));
    }

    FrameworkElement BuildGrip()
    {
        var grip = new Border
        {
            Child = Ui.Icon(Icons.Grip, 16, 2.75),
            Width = 18,
            Background = Brushes.Transparent, // para recibir el ratón en toda su altura
            Cursor = Cursors.SizeAll,
            ToolTip = "Arrastra para mover la barra",
            Margin = new Thickness(0, 0, 2, 0),
        };
        grip.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextoSecundario");
        grip.MouseLeftButtonDown += (s, e) =>
        {
            _barGrab = e.GetPosition(_floatingBar);
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (s, e) =>
        {
            if (!grip.IsMouseCaptured || _placeRoot == null) return;
            var p = e.GetPosition(_placeRoot);
            _barWanted = new Point(p.X - _barGrab.X, p.Y - _barGrab.Y);
            _barMoved = true;
            PlaceFloatingBar();
        };
        grip.MouseLeftButtonUp += (s, e) => grip.ReleaseMouseCapture();
        return grip;
    }

    static Border PlaceDivider()
    {
        var d = Ui.Divider();
        d.Height = 20;
        d.Margin = new Thickness(6, 0, 6, 0);
        return d;
    }

    /// <summary>
    /// Color o grosor contraído: un recuadro con borde, la vista previa y una flechita hacia la
    /// derecha (hacia donde se despliega). Así se lee como algo que se abre, no como una muestra más.
    /// </summary>
    static ToggleButton PickerButton(UIElement preview)
    {
        var chevron = Ui.Icon(Icons.ChevronRight, 12, 2);
        chevron.Margin = new Thickness(3, 0, 0, 0);
        chevron.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextoSecundario");
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(preview);
        content.Children.Add(chevron);
        var frame = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 0, 3, 0),
            Height = 28,
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Borde");
        var b = new ToggleButton { Content = frame, Width = double.NaN, Height = 30 };
        b.SetResourceReference(StyleProperty, "EstiloSegmento");
        return b;
    }

    static Button PlaceIconBtn(string icon, string tip, Action action)
    {
        var b = Ui.IconBtn(icon, tip, action ?? (() => { }));
        b.Width = b.Height = 32;
        return b;
    }

    /// <summary>Lo de poco uso: pasar a una pestaña, guardar como, quitar el recorte, limpiar y ajustes.</summary>
    void ShowPlaceMenu(Button target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        RefocusAfter(menu);
        MenuItem Item(string icon, string name, string key, Action action)
        {
            var item = new MenuItem { Header = MenuHeader(icon, name, key) };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
            return item;
        }
        Item(Icons.Link, "Abrir en pestaña", "", () => _owner.OpenInTab(this));
        Item(Icons.Save, "Guardar como…", "Ctrl+S", Save);
        if (IsCropped) Item(Icons.Crop, "Quitar recorte", "", RemoveCrop);
        menu.Items.Add(MenuSeparator());
        Item(Icons.Trash, "Quitar todas las anotaciones", "", ClearAll);
        menu.Items.Add(MenuSeparator());
        Item(Icons.Settings, "Ajustes", "Ctrl+,", () => SettingsWindow.Show(_owner.Window));
        menu.IsOpen = true;
    }

    /// <summary>Despliega los colores o los grosores (el otro se contrae); otro clic los contrae.</summary>
    void TogglePicker(bool colors)
    {
        var panel = colors ? _placeColors : _placeSizes;
        bool open = panel.Visibility != Visibility.Visible;
        ShowPickers(colors && open, !colors && open);
    }

    void CollapsePickers()
    {
        if (_floatingBar != null) ShowPickers(false, false);
    }

    void ShowPickers(bool colors, bool sizes)
    {
        _placeColors.Visibility = colors ? Visibility.Visible : Visibility.Collapsed;
        _placeColorButton.Visibility = colors ? Visibility.Collapsed : Visibility.Visible;
        _placeSizes.Visibility = sizes ? Visibility.Visible : Visibility.Collapsed;
        _placeSizeButton.Visibility = sizes ? Visibility.Collapsed : Visibility.Visible;
        // El botón contraído no queda «marcado»: solo muestra lo actual
        _placeColorButton.IsChecked = false;
        _placeSizeButton.IsChecked = false;
    }

    /// <summary>El botón contraído de color y el de grosor muestran lo actual (desde RefreshSwatches y RefreshSizes).</summary>
    void RefreshPlacePickers()
    {
        if (_placeSizeDots == null) return; // la barra todavía no se armó (o se está armando)
        var current = CurrentColor;
        _placeColorDot.Fill = new SolidColorBrush(current);
        var others = Palette.Select(p => p.color).Where(c => c != current).ToArray();
        for (int i = 0; i < _placeColorMinis.Length; i++) _placeColorMinis[i].Fill = new SolidColorBrush(others[i]);
        var name = Palette.FirstOrDefault(p => p.color == current).name ?? "Color";
        _placeColorButton.ToolTip = $"{name} (clic para ver todos los colores)";
        System.Windows.Automation.AutomationProperties.SetName(_placeColorButton, $"Color: {name}");
        // El actual con el color del ícono; los otros dos, tenues
        double dot = DotFor(_size);
        foreach (var d in _placeSizeDots) d.Opacity = d.Width == dot ? 1 : 0.3;
        _placeSizeButton.ToolTip = $"Grosor: {_size} px (clic para ver los tres; teclas 1–3)";
        System.Windows.Automation.AutomationProperties.SetName(_placeSizeButton, $"Grosor: {_size} px");
    }

    /// <summary>El punto que representa un grosor en la barra (como los tres de la pestaña).</summary>
    static double DotFor(double size) => size <= SizePresets[0] ? 4 : size <= SizePresets[1] ? 7 : 10;

    /// <summary>Atenúa la barra mientras se dibuja, con un fundido corto si Windows tiene las animaciones.</summary>
    void DimPlaceBar(bool dim)
    {
        dim &= _inPlace;
        if (_floatingBar == null || dim == _barDimmed) return;
        _barDimmed = dim;
        double to = dim ? DimmedBar : 1;
        if (!SystemParameters.ClientAreaAnimation)
        {
            _floatingBar.BeginAnimation(OpacityProperty, null);
            _floatingBar.Opacity = to;
            return;
        }
        _floatingBar.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(dim ? 120 : 180),
        });
    }
}
