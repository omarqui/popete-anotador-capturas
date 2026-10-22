// ----------------------------------------------------------------------------
//  Editor
// ----------------------------------------------------------------------------
enum Tool { Select, Pen, Highlighter, Line, Arrow, Rectangle, Text, Eraser }

sealed partial class EditorWindow : Window
{
    /// <summary>Editores abiertos por Id de sesión (para no abrir la misma captura dos veces).</summary>
    public static readonly Dictionary<string, EditorWindow> Open = new Dictionary<string, EditorWindow>();

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

    readonly Session _session;
    readonly BitmapSource _image;
    readonly Grid _surface;          // anotaciones (tinta + textos) sobre la captura y su margen
    readonly InkCanvas _ink;
    readonly Canvas _textLayer;
    readonly ScrollViewer _scroll;
    readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
    readonly Border _flash;
    readonly Grid _frame;            // captura + _flash, con esquinas redondeadas en pantalla
    readonly double _pad;            // margen dibujable alrededor de la captura, en píxeles de la captura

    /// <summary>Espacio de lienzo visible alrededor de la captura al ajustar, en DIP de pantalla.</summary>
    const double ViewMargin = 48;

    /// <summary>Radio de las esquinas de la captura en el editor, en píxeles de pantalla (como macOS).</summary>
    const double CornerRadiusOnScreen = 10;
    readonly DispatcherTimer _saveTimer;
    readonly Stack<IUndo> _undo = new Stack<IUndo>();

    Tool _tool = Tool.Pen;
    // Cada familia (lápiz/formas/texto y resaltador) tiene color principal y secundario
    Color _penColor = Palette[0].color;          // rojo
    Color _penColor2 = Palette[3].color;         // azul
    Color _highlightColor = Palette[1].color;    // amarillo
    Color _highlightColor2 = Palette[2].color;   // verde
    bool _altAlone;      // Alt presionado sin otra tecla ni clic: al soltarlo se intercambian los colores
    double _size = 4;
    bool _suppress;      // no registrar cambios internos en el historial de deshacer
    bool _pendingSave;
    bool _closeScheduled;
    Stroke _preview;
    Point _start;
    TextBox _editing;

    Color CurrentColor
    {
        get => _tool == Tool.Highlighter ? _highlightColor : _penColor;
        set { if (_tool == Tool.Highlighter) _highlightColor = value; else _penColor = value; }
    }

    Color SecondaryColor
    {
        get => _tool == Tool.Highlighter ? _highlightColor2 : _penColor2;
        set { if (_tool == Tool.Highlighter) _highlightColor2 = value; else _penColor2 = value; }
    }

    public EditorWindow(Session session)
    {
        _session = session;
        _image = History.LoadImage(session.OriginalPath);
        int pw = _image.PixelWidth, ph = _image.PixelHeight;

        Title = $"{AppInfo.Name} — captura del {session.Meta.Created:dd/MM/yyyy HH:mm} — {pw}×{ph}";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(work.Width * 0.85, Math.Max(760, pw + 80));
        Height = Math.Min(work.Height * 0.9, Math.Max(480, ph + 170));
        Width = Math.Max(Width, Math.Min(work.Width * 0.92, 1380)); // que la barra quepa en una fila
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Attach(this);
        UseLayoutRounding = true;

        // Guardado automático en el historial, poco después de cada cambio
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (s, e) => SaveSession();

        // ---- superficie de dibujo (1 DIP = 1 píxel de la captura) ----
        // Alrededor de la captura hay un margen donde también se puede dibujar;
        // al exportar, la imagen crece lo justo para incluir lo que quede afuera.
        _pad = Math.Round(Math.Clamp(Math.Max(pw, ph) * 0.3, 200, 600));
        double bw = pw + 2 * _pad, bh = ph + 2 * _pad;

        var imgCtl = new Image { Source = _image, Width = pw, Height = ph, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(imgCtl, BitmapScalingMode.HighQuality);

        _ink = new InkCanvas { Background = Brushes.Transparent, Width = bw, Height = bh };
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (_suppress) return;
            PushUndo(new StrokeChange(_ink.Strokes, e.Added, e.Removed));
        };
        _ink.PreviewMouseLeftButtonDown += Ink_MouseDown;
        _ink.PreviewMouseMove += Ink_MouseMove;
        _ink.PreviewMouseLeftButtonUp += Ink_MouseUp;

        _textLayer = new Canvas { Width = bw, Height = bh, IsHitTestVisible = false };

        // Anotaciones (tinta + textos) sobre toda la tabla, margen incluido
        _surface = new Grid { Width = bw, Height = bh };
        _surface.Children.Add(_ink);
        _surface.Children.Add(_textLayer);

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
        _frame = new Grid { Width = pw, Height = ph };
        _frame.Children.Add(imgCtl);
        _frame.Children.Add(_flash);
        var framed = new Border
        {
            Child = _frame,
            Margin = new Thickness(_pad),
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

        var board = new Grid { Width = bw, Height = bh };
        board.Children.Add(limit);
        board.Children.Add(framed);
        board.Children.Add(_surface);
        board.Children.Add(BuildSelectionLayer(bw, bh));

        // Lo recién dibujado con lápiz o resaltador queda seleccionado
        _ink.StrokeCollected += (s, e) => SelectStrokes(new StrokeCollection { e.Stroke });

        // Clic derecho sobre el lienzo: intercambia color principal y secundario (como tocar Alt)
        _ink.PreviewMouseRightButtonUp += (s, e) =>
        {
            if (_editing != null) return;
            SwapColors();
            e.Handled = true;
        };

        // El host mide lo que la captura: el margen queda por fuera del layout
        // (Canvas no recorta), así "Ajustar" encuadra la captura y no la tabla entera.
        var host = new Canvas
        {
            Width = pw,
            Height = ph,
            LayoutTransform = _zoom,
            Margin = new Thickness(ViewMargin),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Canvas.SetLeft(board, -_pad);
        Canvas.SetTop(board, -_pad);
        host.Children.Add(board);

        _scroll = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.SetResourceReference(BackgroundProperty, "Lienzo");
        _scroll.PreviewMouseWheel += (s, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                SetZoom(_zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
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
        root.Children.Add(canvasArea);
        Content = root;

        LoadAnnotations();
        RefreshSizes();
        SetTool(Tool.Pen);

        // PreviewKeyDown: la ventana ve las teclas antes que el InkCanvas,
        // que de otro modo se queda con Ctrl+C / Ctrl+X / Ctrl+V.
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        PreviewMouseDown += (s, e) => _altAlone = false; // Alt+clic no cuenta como toque de Alt
        Deactivated += (s, e) => _altAlone = false;      // Alt+Tab: el Alt se suelta en otra ventana

        Open[session.Meta.Id] = this;
        Closed += (s, e) => Open.Remove(session.Meta.Id);

        Loaded += (s, e) =>
        {
            FitZoom();
            // Traer al frente aunque la app esté en segundo plano
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        };
        Closing += (s, e) =>
        {
            CommitText();
            if (_pendingSave) SaveSession();
        };
    }

    // ------------------------------------------------------------------ persistencia
    // En disco, trazos y textos se guardan relativos a la esquina de la captura
    // (pueden ser negativos si están en el margen); en pantalla están corridos _pad.
    Matrix ToBoard => new Matrix(1, 0, 0, 1, _pad, _pad);
    Matrix ToCapture => new Matrix(1, 0, 0, 1, -_pad, -_pad);

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
                    _ink.Strokes.Add(strokes);
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

    void MarkChanged()
    {
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
                FontSize = tb.FontSize,
                Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? Palette[0].color.ToString(),
            }).ToList();
            var json = JsonSerializer.SerializeToUtf8Bytes(notes, History.JsonOptions);
            History.WriteAtomic(_session.TextsPath, fs => fs.Write(json, 0, json.Length));

            var rendered = Render();
            History.SavePng(rendered, _session.PreviewPath);
            History.SaveThumb(rendered, _session.ThumbPath);

            _session.Meta.Updated = DateTime.Now;
            History.SaveMeta(_session);

            _pendingSave = false;
            ShowSaved($"Guardado en historial {DateTime.Now:HH:mm:ss}", true);
        }
        catch (Exception ex)
        {
            ShowSaved("Error al guardar: " + ex.Message, false);
        }
    }

    // ------------------------------------------------------------------ deshacer
    void PushUndo(IUndo item)
    {
        _undo.Push(item);
        MarkChanged();
    }

    void Undo()
    {
        CommitText();
        ClearSelection();
        if (_undo.Count == 0) return;
        _suppress = true;
        try { _undo.Pop().Undo(); }
        finally { _suppress = false; }
        MarkChanged();
    }

    void ClearAll()
    {
        CommitText();
        ClearSelection();
        var strokes = new StrokeCollection(_ink.Strokes);
        var elements = _textLayer.Children.Cast<UIElement>().ToList();
        if (strokes.Count == 0 && elements.Count == 0) return;

        _suppress = true;
        _ink.Strokes.Clear();
        _textLayer.Children.Clear();
        _suppress = false;
        PushUndo(new ClearAllChange(_ink.Strokes, strokes, _textLayer, elements));
    }

    // ------------------------------------------------------------------ zoom
    double DpiScale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.05, 8);
        _zoom.ScaleX = _zoom.ScaleY = z;
        _zoomText.Text = $"{Math.Round(z * DpiScale() * 100)}%";

        // El recorte está dentro del zoom: el radio se divide para que en pantalla mida siempre lo mismo
        double r = CornerRadiusOnScreen / z;
        _frame.Clip = new RectangleGeometry(new Rect(0, 0, _image.PixelWidth, _image.PixelHeight), r, r);
        UpdateSelectionVisual();
        UpdatePointer();
    }

    void FitZoom()
    {
        double availW = Math.Max(100, _scroll.ActualWidth - 2 * ViewMargin - 20);
        double availH = Math.Max(100, _scroll.ActualHeight - 2 * ViewMargin - 20);
        double fit = Math.Min(availW / _image.PixelWidth, availH / _image.PixelHeight);
        // Nunca agrandar por encima del tamaño real en pantalla
        SetZoom(Math.Min(1 / DpiScale(), fit));
    }

    // ------------------------------------------------------------------ teclado
    /// <summary>Tocar Alt (presionar y soltar sin nada en medio) intercambia color principal y secundario.</summary>
    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.System || e.SystemKey is not (Key.LeftAlt or Key.RightAlt)) return;
        e.Handled = true; // que Alt no active el modo menú de la ventana
        if (!_altAlone || _editing != null) return;
        _altAlone = false;
        SwapColors();
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing != null) return; // escribiendo texto

        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            // Solo un Alt "suelto" (sin Ctrl: AltGr llega como Ctrl+Alt) puede intercambiar colores
            if (!e.IsRepeat) _altAlone = Keyboard.Modifiers == ModifierKeys.Alt;
            e.Handled = true;
            return;
        }
        _altAlone = false; // Alt+otra tecla (Alt+Tab, Alt+F4...) no intercambia

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(); break;
                case Key.C: if (CopyToClipboard()) CloseAfterFlash(); break;
                case Key.S: Save(); break;
                case Key.Enter: if (CopyToClipboard()) Close(); break;
                case Key.H: ToggleRecents(); break;
                case Key.D0: case Key.NumPad0: FitZoom(); break;
                case Key.OemPlus: case Key.Add: SetZoom(_zoom.ScaleX * 1.15); break;
                case Key.OemMinus: case Key.Subtract: SetZoom(_zoom.ScaleX / 1.15); break;
                default: return;
            }
            e.Handled = true;
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
            case Key.T: SetTool(Tool.Text); break;
            case Key.E: SetTool(Tool.Eraser); break;
            case Key.D1: case Key.NumPad1: SetSize(SizePresets[0]); break;
            case Key.D2: case Key.NumPad2: SetSize(SizePresets[1]); break;
            case Key.D3: case Key.NumPad3: SetSize(SizePresets[2]); break;
            case Key.D4: case Key.NumPad4: SetSize(SizePresets[3]); break;
            case Key.Delete: case Key.Back: DeleteSelection(); break;
            case Key.Escape: if (HasSelection) ClearSelection(); else Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
