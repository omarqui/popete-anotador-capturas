// ----------------------------------------------------------------------------
//  Editor de una captura: el contenido de una pestaña de EditorShell
//
//  Lo de ventana (título, tamaño, foco, cerrar) es de EditorShell. La pestaña
//  avisa con Closed cuando se cierra (para soltar eventos estáticos) y pide
//  salir con Dismiss (copiar y cerrar, Esc, cierre automático).
// ----------------------------------------------------------------------------
enum Tool { Select, Pen, Highlighter, Line, Arrow, Rectangle, Text, Eraser, Crop, Ellipse }

sealed partial class EditorView : UserControl
{
    static readonly (Color color, string name)[] Palette =
    {
        (Color.FromRgb(232, 17, 35), "Rojo"),
        (Color.FromRgb(255, 214, 0), "Amarillo"),
        (Color.FromRgb(0, 168, 80), "Verde"),
        (Color.FromRgb(0, 120, 215), "Azul"),
        (Color.FromRgb(142, 63, 208), "Morado"),
        (Color.FromRgb(255, 140, 0), "Naranja"),
        (Colors.Black, "Negro"),
        (Colors.White, "Blanco"),
    };

    readonly EditorShell _shell;     // la ventana: dueña de los diálogos y de la salida rápida
    readonly Session _session;
    readonly BitmapSource _original; // la captura tal como llegó; nunca se modifica
    BitmapSource _image;             // lo que se ve y se exporta: la captura, recortada si hay recorte
    Int32Rect _crop;                 // recorte actual en píxeles de la captura original (o la captura entera)
    readonly Grid _surface;          // anotaciones (tinta + textos + números) sobre la captura y su margen
    readonly InkCanvas _ink;
    readonly Canvas _textLayer;
    readonly ScrollViewer _scroll;
    readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
    readonly Border _flash;
    readonly Grid _frame;            // captura + _flash, con esquinas redondeadas en pantalla
    readonly Image _imageCtl;
    readonly Border _framed;         // _frame con su sombra, ubicado en VisibleRect
    readonly Grid _board;            // captura + margen
    readonly Canvas _host;           // mide lo que la captura visible; la tabla cuelga de él
    readonly double _pad;            // margen dibujable alrededor de la captura, en píxeles de la captura

    /// <summary>Espacio de lienzo visible alrededor de la captura al ajustar, en DIP de pantalla.</summary>
    const double ViewMargin = 48;

    /// <summary>Radio de las esquinas de la captura en el editor, en píxeles de pantalla (como macOS).</summary>
    const double CornerRadiusOnScreen = 10;
    readonly DispatcherTimer _saveTimer;
    readonly Stack<IUndo> _undo = new Stack<IUndo>();
    readonly Stack<IUndo> _redo = new Stack<IUndo>();

    Tool _tool = Tool.Pen;
    // Cada familia (lápiz/formas/texto y resaltador) tiene su color
    Color _penColor = Palette[0].color;          // rojo
    Color _highlightColor = Palette[1].color;    // amarillo
    double _size = 4;
    bool _suppress;      // no registrar cambios internos en el historial de deshacer
    bool _pendingSave;
    bool _closeScheduled;
    bool _closed;        // su pestaña ya se cerró
    Stroke _preview;
    Point _start;
    TextBox _editing;
    TextBlock _editingBlock; // texto existente que se está corrigiendo (doble clic)

    Color CurrentColor
    {
        get => _tool == Tool.Highlighter ? _highlightColor : _penColor;
        set { if (_tool == Tool.Highlighter) _highlightColor = value; else _penColor = value; }
    }

    public Session Session => _session;

    /// <summary>Se cerró su pestaña (o la ventana): para soltar las suscripciones a eventos estáticos.</summary>
    public event EventHandler Closed;

    /// <summary>Después de guardar en el historial (la pestaña actualiza su miniatura).</summary>
    public event Action Saved;

    public EditorView(EditorShell shell, Session session)
    {
        _shell = shell;
        _session = session;
        _original = History.LoadImage(session.OriginalPath);
        int pw = _original.PixelWidth, ph = _original.PixelHeight;
        _crop = ValidCrop(session.Meta.Crop) ?? new Int32Rect(0, 0, pw, ph);
        _image = CropOf(_crop);

        UseLayoutRounding = true;
        Focusable = true; // sin un control con foco adentro, las teclas no llegarían a PreviewKeyDown
        FocusVisualStyle = null;

        // Guardado automático en el historial, poco después de cada cambio
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (s, e) => SaveSession();

        // ---- superficie de dibujo (1 DIP = 1 píxel de la captura) ----
        // Alrededor de la captura hay un margen donde también se puede dibujar;
        // al exportar, la imagen crece lo justo para incluir lo que quede afuera.
        // Los tamaños de todo esto los fija LayoutForImage (cambian al recortar).
        _pad = Math.Round(Math.Clamp(Math.Max(pw, ph) * 0.3, 200, 600));

        _imageCtl = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_imageCtl, BitmapScalingMode.HighQuality);

        _ink = new InkCanvas { Background = Brushes.Transparent };
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (_suppress) return;
            // Un trazo recién hecho a mano: StrokeCollected lo descarta (si es mínimo) o lo
            // cambia por un NoteStroke y registra ahí el deshacer
            if (e.Added.Count == 1 && e.Removed.Count == 0 && e.Added[0] is not NoteStroke) return;
            PushUndo(new StrokeChange(_ink.Strokes, e.Added, e.Removed));
        };
        _ink.PreviewMouseLeftButtonDown += Ink_MouseDown;
        _ink.PreviewMouseMove += Ink_MouseMove;
        _ink.PreviewMouseLeftButtonUp += Ink_MouseUp;

        // Sin fondo: solo los textos reciben clics (doble clic para corregir); el resto pasa a la tinta
        _textLayer = new Canvas();

        // Anotaciones (tinta + textos + números) sobre toda la tabla, margen incluido
        _surface = new Grid();
        _surface.Children.Add(_ink);
        _surface.Children.Add(_textLayer);
        _surface.Children.Add(BuildNumbersLayer());

        // La captura en el centro, con esquinas redondeadas y sombra (solo en pantalla).
        // Capa de parpadeo encima: no forma parte de la imagen exportada.
        _flash = new Border
        {
            Background = Brushes.White,
            Opacity = 0,
            IsHitTestVisible = false,
            BorderThickness = new Thickness(6),
        };
        _flash.SetResourceReference(Border.BorderBrushProperty, "Acento");
        _frame = new Grid();
        _frame.Children.Add(_imageCtl);
        _frame.Children.Add(_flash);
        _framed = new Border
        {
            Child = _frame,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25 },
        };

        // Borde punteado que marca hasta dónde se puede dibujar
        var limit = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 12,
            RadiusY = 12,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false,
            Opacity = 0.7,
        };
        limit.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "BordeFuerte");

        _board = new Grid();
        _board.Children.Add(limit);
        _board.Children.Add(_framed);
        _board.Children.Add(_surface);
        _board.Children.Add(BuildIdeaLayer()); // campo y marcas de las ideas: fuera de _surface, no se exportan
        _board.Children.Add(BuildSelectionLayer());

        // Lo recién dibujado con lápiz o resaltador queda seleccionado
        _ink.StrokeCollected += (s, e) =>
        {
            if (IsTiny(e.Stroke))
            {
                // Un clic o un trazo de pocos píxeles: casi siempre es accidental, no se aplica
                _suppress = true;
                _ink.Strokes.Remove(e.Stroke);
                _suppress = false;
                return;
            }
            // El InkCanvas agrega un trazo común: se cambia por uno con borde blanco
            var stroke = NoteStroke.From(e.Stroke);
            _suppress = true;
            _ink.Strokes.Replace(e.Stroke, new StrokeCollection { stroke });
            _suppress = false;
            PushUndo(new StrokeChange(_ink.Strokes, new StrokeCollection { stroke }, new StrokeCollection()));
            SelectStrokes(new StrokeCollection { stroke }, auto: true);
            AdvanceColor();
            OfferIdea(stroke);
        };

        // El host mide lo que la captura: el margen queda por fuera del layout
        // (Canvas no recorta), así "Ajustar" encuadra la captura y no la tabla entera.
        _host = new Canvas
        {
            LayoutTransform = _zoom,
            RenderTransform = _panShift, // pan cuando la captura entra entera (ver EditorView.Pan.cs)
            Margin = new Thickness(ViewMargin),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _host.Children.Add(_board);
        LayoutForImage();

        _scroll = new ScrollViewer
        {
            Content = _host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.SetResourceReference(BackgroundProperty, "Lienzo");
        _scroll.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                StepZoom(e.Delta > 0 ? 1 : -1);
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta); // Shift+rueda: horizontal
            else
                return;
            e.Handled = true;
        };

        // ---- layout ----
        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var statusBar = BuildStatusBar();
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        var recents = BuildRecents();
        DockPanel.SetDock(recents, Dock.Bottom);
        root.Children.Add(recents);

        // El puntero va en una capa sobre el lienzo, fuera del zoom
        var canvasArea = new Grid();
        canvasArea.Children.Add(_scroll);
        canvasArea.Children.Add(BuildPointerLayer());
        canvasArea.Children.Add(BuildIdeaOverlay()); // la tarjeta de la idea, fuera del zoom (EditorView.IdeaCard.cs)
        root.Children.Add(canvasArea);
        Content = root;

        _size = AppSettings.Current.GrosorInicial;
        LoadAnnotations();
        InitOrders();
        RefreshNumbers();
        RefreshSizes();
        // La última herramienta usada, aunque se haya reiniciado la app (Recortar no: es de una captura)
        var last = AppSettings.Current.UltimaHerramienta;
        SetTool(Enum.IsDefined(last) && last != Tool.Crop ? last : Tool.Pen);

        // PreviewKeyDown: el editor ve las teclas antes que el InkCanvas,
        // que de otro modo se queda con Ctrl+C / Ctrl+X / Ctrl+V.
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        EnableDrop();
        PreviewMouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle && IsInside(e.OriginalSource, _scroll))
            {
                BeginPan(e.GetPosition(_scroll));
                e.Handled = true;
            }
            // Doble clic sobre un trazo: lo elige (con sus asas), con cualquier herramienta, y el
            // segundo clic no llega a la tinta. Sobre un texto lo edita (EditText), sobre un número
            // lo devuelve a su lugar (NumberDown), y con un cuadro de texto abierto selecciona palabras.
            else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && _editing == null && !_cropMode
                     && IsInside(e.OriginalSource, _scroll) && !IsInside(e.OriginalSource, _textLayer)
                     && !IsInside(e.OriginalSource, _numbersLayer) && !IsInside(e.OriginalSource, _ideaLayer) // el número fantasma
                     && HandleAt(e.GetPosition(_ink)) == null
                     && StrokeUnder(e.GetPosition(_ink)) is Stroke hit)
            {
                SelectOnly(hit);
                e.Handled = true;
            }
        };
        // Clic derecho en el lienzo: copiar (y cerrar, según el ajuste), como Ctrl+C
        PreviewMouseRightButtonUp += (s, e) =>
        {
            // Con un texto o una idea a medio escribir, no (como cualquier cuadro de texto); sobre el fantasma tampoco
            if (_editing != null || _ideaBox is { Text.Length: > 0 } || IsInside(e.OriginalSource, _ideaLayer)
                || !IsInside(e.OriginalSource, _scroll)) return;
            CopyAndMaybeClose();
            e.Handled = true;
        };
        _scroll.PreviewMouseMove += (s, e) => { if (PanMove(e.GetPosition(_scroll))) e.Handled = true; };
        _scroll.PreviewMouseUp += (s, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle || _panStart == null) return;
            EndPan();
            e.Handled = true;
        };
        _scroll.LostMouseCapture += (s, e) => EndPan();

        // La pestaña se crea al elegirla: ya está visible cuando se carga
        Loaded += (s, e) => FitZoom();
    }

    // ------------------------------------------------------------------ pestaña
    /// <summary>
    /// La pestaña pasó al frente. Toma la herramienta, los colores y el grosor de la que se
    /// estaba usando (<paramref name="previous"/>), y el panel de recientes de toda la ventana.
    /// </summary>
    public void OnSelected(EditorView previous)
    {
        if (previous != null && previous != this) AdoptStyle(previous);
        if ((_recents.Visibility == Visibility.Visible) != _recentsShown) ToggleRecents(_recentsShown);
        else if (_recentsShown) LoadRecents(); // la marca de "esta captura" y las abiertas cambian
        Dispatcher.InvokeAsync(() => Focus(), DispatcherPriority.Input);
    }

    /// <param name="byUser">Cambió de pestaña el usuario: ya está trabajando en la ventana.</param>
    public void OnDeselected(bool byUser)
    {
        CommitText();
        if (_cropMode) ConfirmCrop(); // como cambiar de herramienta: aplica el marco
        if (byUser) MarkTouched();    // una captura nueva ya no se cierra sola
    }

    void AdoptStyle(EditorView from)
    {
        _penColor = from._penColor;
        _highlightColor = from._highlightColor;
        _size = from._size; // sin SetSize: no debe cambiar el grosor de lo que esté seleccionado acá
        RefreshSizes();
        if (_tool != from._tool) SetTool(from._tool);
        RefreshSwatches();
        UpdateDrawingAttributes();
        UpdatePointer();
    }

    /// <summary>Antes de cerrar la pestaña o la ventana: termina el texto, aplica el recorte y guarda lo pendiente.</summary>
    public void PrepareClose()
    {
        CommitText();
        if (_cropMode) ConfirmCrop(); // cerrar con el marco ajustado lo aplica
        if (_pendingSave) SaveSession();
    }

    /// <summary>La pestaña se cerró (después de PrepareClose).</summary>
    public void NotifyClosed()
    {
        if (_closed) return;
        _closed = true;
        _saveTimer.Stop();
        _autoCloseTimer?.Stop();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Salida rápida (copiar y cerrar, Esc, cierre automático): ver EditorShell.Dismiss.</summary>
    void Dismiss()
    {
        if (!_closed) _shell.Dismiss(this);
    }

    // ------------------------------------------------------------------ persistencia
    // La tabla abarca la captura ORIGINAL más _pad alrededor, y no se mueve nunca:
    // recortar solo cambia qué parte se ve (VisibleRect). En disco, trazos y textos
    // se guardan relativos a la esquina de la captura original (pueden ser negativos
    // si están en el margen); en la tabla están corridos _pad.
    Matrix ToBoard => new Matrix(1, 0, 0, 1, _pad, _pad);
    Matrix ToCapture => new Matrix(1, 0, 0, 1, -_pad, -_pad);

    /// <summary>Lo visible de la captura (el recorte), en coordenadas de la tabla.</summary>
    Rect VisibleRect => new Rect(_pad + _crop.X, _pad + _crop.Y, _crop.Width, _crop.Height);

    /// <summary>Ajusta tamaños y posiciones a la parte visible (al abrir y al recortar).</summary>
    void LayoutForImage()
    {
        var v = ShownRect; // con Recortar activo, la captura entera
        double bw = _original.PixelWidth + 2 * _pad, bh = _original.PixelHeight + 2 * _pad;
        _imageCtl.Source = ShownImage;
        _imageCtl.Width = _frame.Width = v.Width;
        _imageCtl.Height = _frame.Height = v.Height;
        _framed.Margin = new Thickness(v.X, v.Y, 0, 0);
        foreach (var e in new FrameworkElement[] { _board, _surface, _ink, _textLayer, _numbersLayer, _ideaLayer, _selectionLayer })
        {
            e.Width = bw;
            e.Height = bh;
        }
        // El host mide lo visible y la tabla cuelga de él de modo que lo visible quede en (0, 0)
        _host.Width = v.Width;
        _host.Height = v.Height;
        Canvas.SetLeft(_board, -v.X);
        Canvas.SetTop(_board, -v.Y);
        UpdateFrameClip();
    }

    /// <summary>Esquinas redondeadas de la captura; el radio se divide por el zoom para medir siempre lo mismo en pantalla.</summary>
    void UpdateFrameClip()
    {
        double r = CornerRadiusOnScreen / _zoom.ScaleX;
        _frame.Clip = new RectangleGeometry(new Rect(0, 0, ShownImage.PixelWidth, ShownImage.PixelHeight), r, r);
    }

    void LoadAnnotations()
    {
        _suppress = true;
        try
        {
            if (File.Exists(_session.InkPath))
            {
                try
                {
                    using var fs = File.OpenRead(_session.InkPath);
                    var strokes = new StrokeCollection(fs);
                    strokes.Transform(ToBoard, false);
                    _ink.Strokes.Add(new StrokeCollection(strokes.Select(NoteStroke.From))); // con borde blanco
                }
                catch { /* archivo dañado: se ignora */ }
            }

            if (File.Exists(_session.TextsPath))
            {
                try
                {
                    var notes = JsonSerializer.Deserialize<List<TextNote>>(File.ReadAllText(_session.TextsPath));
                    foreach (var n in notes ?? new List<TextNote>())
                    {
                        n.X += _pad;
                        n.Y += _pad;
                        AddTextBlock(n);
                    }
                }
                catch { /* archivo dañado: se ignora */ }
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>Trazo a mano que en pantalla no llega a 5 px de largo ni de alto (un punto, un temblor del clic).</summary>
    bool IsTiny(Stroke s)
    {
        if (s == _preview || s.StylusPoints.Count == 0) return false;
        double minX = s.StylusPoints.Min(p => p.X), maxX = s.StylusPoints.Max(p => p.X);
        double minY = s.StylusPoints.Min(p => p.Y), maxY = s.StylusPoints.Max(p => p.Y);
        return Math.Max(maxX - minX, maxY - minY) * _zoom.ScaleX < 5;
    }

    void MarkChanged()
    {
        MarkTouched(); // ya no se cierra solo (EditorView.AutoClose.cs)
        RefreshNumbers();
        _pendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveSession()
    {
        _saveTimer.Stop();
        if (_editing != null)
        {
            // Se está escribiendo un texto: se guarda cuando termine.
            _saveTimer.Start();
            return;
        }
        try
        {
            if (_ink.Strokes.Count > 0)
            {
                var strokes = _ink.Strokes.Clone();
                strokes.Transform(ToCapture, false);
                History.WriteAtomic(_session.InkPath, fs => strokes.Save(fs));
            }
            else if (File.Exists(_session.InkPath))
                File.Delete(_session.InkPath);

            var notes = _textLayer.Children.OfType<TextBlock>().Select(tb => new TextNote
            {
                Text = tb.Text,
                X = Canvas.GetLeft(tb) - _pad,
                Y = Canvas.GetTop(tb) - _pad,
                Orden = tb.Tag is long orden ? orden : 0,
                Fondo = tb is NoteText nt && nt.Fondo,
                NumeroDx = NumberOffsetOf(tb).X,
                NumeroDy = NumberOffsetOf(tb).Y,
                Idea = IdeaOf(tb),
                FontSize = tb.FontSize,
                Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? Palette[0].color.ToString(),
            }).ToList();
            var json = JsonSerializer.SerializeToUtf8Bytes(notes, History.JsonOptions);
            History.WriteAtomic(_session.TextsPath, fs => fs.Write(json, 0, json.Length));

            // Las ideas con su encabezado: copiar con ideas pega la ruta de este archivo (Ideas.ForAi)
            var ideas = IdeasText();
            if (ideas != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(ideas);
                History.WriteAtomic(_session.IdeasPath, fs => fs.Write(bytes, 0, bytes.Length));
            }
            else if (File.Exists(_session.IdeasPath))
                File.Delete(_session.IdeasPath);

            var rendered = Render();
            History.SavePng(rendered, _session.PreviewPath);
            History.SaveThumb(rendered, _session.ThumbPath);

            _session.Meta.Updated = DateTime.Now;
            History.SaveMeta(_session);

            _pendingSave = false;
            ShowSaved($"Guardado en historial {DateTime.Now:HH:mm:ss}", true);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ShowSaved("Error al guardar: " + ex.Message, false);
        }
    }

    // ------------------------------------------------------------------ deshacer / rehacer
    void PushUndo(IUndo item)
    {
        _undo.Push(item);
        _redo.Clear(); // un cambio nuevo invalida lo que se podía rehacer
        MarkChanged();
    }

    void Undo()
    {
        CommitText();
        ClearSelection();
        if (ResetPendingCrop()) return; // con Recortar: primero devuelve el marco a su lugar
        if (_undo.Count == 0) return;
        var item = _undo.Pop();
        _suppress = true;
        try { item.Undo(); }
        finally { _suppress = false; }
        _redo.Push(item);
        SyncPendingCrop();
        MarkChanged();
    }

    void Redo()
    {
        CommitText();
        ClearSelection();
        if (_redo.Count == 0) return;
        var item = _redo.Pop();
        _suppress = true;
        try { item.Redo(); }
        finally { _suppress = false; }
        _undo.Push(item); // sin pasar por PushUndo: no hay que vaciar _redo
        SyncPendingCrop();
        MarkChanged();
    }

    void ClearAll()
    {
        CommitText();
        ClearSelection();
        var strokes = new StrokeCollection(_ink.Strokes);
        var elements = _textLayer.Children.Cast<UIElement>().ToList();
        if (strokes.Count == 0 && elements.Count == 0) return;

        int count = strokes.Count + elements.Count;
        var title = count == 1 ? "¿Quitar la anotación?" : $"¿Quitar las {count} anotaciones?";
        if (!ConfirmDialog.Ask(_shell, title, "Se borran todos los trazos y textos de esta captura. Se puede deshacer con Ctrl+Z.", "Quitar"))
            return;

        _suppress = true;
        _ink.Strokes.Clear();
        _textLayer.Children.Clear();
        _suppress = false;
        PushUndo(new ClearAllChange(_ink.Strokes, strokes, _textLayer, elements));
    }

    // ------------------------------------------------------------------ zoom
    /// <summary>
    /// Niveles de acercar y alejar, en píxeles de pantalla por píxel de la captura (1 = 100 %).
    /// Pasan por 100 % exacto y, desde 200 %, por múltiplos enteros: ahí cada píxel sale como un cuadrado parejo.
    /// </summary>
    static readonly double[] ZoomLevels = { 0.1, 0.15, 0.2, 0.25, 1 / 3.0, 0.5, 2 / 3.0, 0.75, 1, 1.25, 1.5, 2, 3, 4, 6, 8 };

    /// <summary>Desde este zoom la captura se dibuja con píxeles nítidos en vez de suavizada.</summary>
    const double PixelZoom = 2;

    double DpiScale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.05, 8);
        _zoom.ScaleX = _zoom.ScaleY = z;
        double real = z * DpiScale();
        _zoomText.Text = $"{Math.Round(real * 100)}%";
        // Suavizar al agrandar deja borrosos el texto y los bordes de 1 px de una captura
        RenderOptions.SetBitmapScalingMode(_imageCtl,
            real >= PixelZoom * 0.999 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

        UpdateFrameClip();
        UpdateSelectionVisual();
        UpdatePointer();
        PlaceIdeaCard();
    }

    void FitZoom()
    {
        double availW = Math.Max(100, _scroll.ActualWidth - 2 * ViewMargin - 20);
        double availH = Math.Max(100, _scroll.ActualHeight - 2 * ViewMargin - 20);
        double fit = Math.Min(availW / ShownImage.PixelWidth, availH / ShownImage.PixelHeight);
        // Nunca agrandar por encima del tamaño real en pantalla
        SetZoom(Math.Min(1 / DpiScale(), fit));
        ResetPanShift(); // "Ajustar" vuelve a centrar
    }

    /// <summary>Pasa al nivel siguiente (+1) o anterior (−1) de <see cref="ZoomLevels"/>; desde un zoom libre (Ajustar), al más cercano en esa dirección.</summary>
    void StepZoom(int direction)
    {
        double dpi = DpiScale(), real = _zoom.ScaleX * dpi;
        double next = direction > 0
            ? ZoomLevels.FirstOrDefault(l => l > real * 1.001, real)
            : ZoomLevels.LastOrDefault(l => l < real / 1.001, real);
        SetZoom(next / dpi);
    }

    // ------------------------------------------------------------------ teclado
    /// <summary>Alt solo no hace nada: que no active el modo menú de la ventana (se tragaría la tecla siguiente).</summary>
    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_ideaBox != null && IdeaKey(e)) return; // escribiendo una idea
        if (_editing != null) return; // escribiendo texto

        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            Redo(); // Ctrl+Shift+Z, como en otros editores
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(); break;
                case Key.Y: Redo(); break;
                case Key.C: CopyAndMaybeClose(); break;
                case Key.S: Save(); break;
                case Key.Enter: if (CopyToClipboard()) Dismiss(); break;
                case Key.H: ToggleRecents(); break;
                case Key.A: SelectAll(); break;
                case Key.OemComma: SettingsWindow.Show(_shell); break;
                case Key.D0: case Key.NumPad0: FitZoom(); break;
                case Key.OemPlus: case Key.Add: StepZoom(1); break;
                case Key.OemMinus: case Key.Subtract: StepZoom(-1); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        // Flechas (Shift: de a 10 px) mueven lo seleccionado
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            if (NudgeSelection(e.Key)) e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Tab: SetTool(_tool == Tool.Pen ? Tool.Arrow : Tool.Pen); break; // alterna lápiz ↔ flecha
            case Key.V: SetTool(Tool.Select); break;
            case Key.P: SetTool(Tool.Pen); break;
            case Key.H: SetTool(Tool.Highlighter); break;
            case Key.L: SetTool(Tool.Line); break;
            case Key.A: SetTool(Tool.Arrow); break;
            case Key.R: SetTool(Tool.Rectangle); break;
            case Key.O: SetTool(Tool.Ellipse); break;
            case Key.T: SetTool(Tool.Text); break;
            case Key.E: SetTool(Tool.Eraser); break;
            case Key.C: SetTool(Tool.Crop); break;
            case Key.N: ToggleNumbers(); break;
            case Key.D1: case Key.NumPad1: SetSize(SizePresets[0]); break;
            case Key.D2: case Key.NumPad2: SetSize(SizePresets[1]); break;
            case Key.D3: case Key.NumPad3: SetSize(SizePresets[2]); break;
            case Key.Delete: case Key.Back: DeleteSelection(); break;
            case Key.Enter when _cropMode: ConfirmCrop(); break;
            case Key.Escape:
                if (_cropMode) CancelCrop();
                else if (HasSelection) ClearSelection();
                else Dismiss();
                break;
            default: return;
        }
        e.Handled = true;
    }
}
