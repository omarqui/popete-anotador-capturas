// ----------------------------------------------------------------------------
//  Editor
// ----------------------------------------------------------------------------
enum Tool { Pen, Highlighter, Line, Arrow, Rectangle, Text, Eraser }

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
    readonly Grid _surface;          // lo que se exporta: imagen + tinta + textos
    readonly InkCanvas _ink;
    readonly Canvas _textLayer;
    readonly ScrollViewer _scroll;
    readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
    readonly Border _flash;
    readonly DispatcherTimer _saveTimer;
    readonly Stack<IUndo> _undo = new Stack<IUndo>();

    Tool _tool = Tool.Pen;
    Color _penColor = Palette[0].color;
    Color _highlightColor = Palette[1].color;
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

    public EditorWindow(Session session)
    {
        _session = session;
        _image = History.LoadImage(session.OriginalPath);
        int pw = _image.PixelWidth, ph = _image.PixelHeight;

        Title = $"{AppInfo.Name} — captura del {session.Meta.Created:dd/MM/yyyy HH:mm} — {pw}×{ph}";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(work.Width * 0.85, Math.Max(760, pw + 80));
        Height = Math.Min(work.Height * 0.9, Math.Max(480, ph + 170));
        Width = Math.Max(Width, Math.Min(work.Width * 0.85, 1100)); // que la barra quepa en una fila
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Attach(this);
        UseLayoutRounding = true;

        // Guardado automático en el historial, poco después de cada cambio
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (s, e) => SaveSession();

        // ---- superficie de dibujo (1 DIP = 1 píxel de la captura) ----
        var imgCtl = new Image { Source = _image, Width = pw, Height = ph, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(imgCtl, BitmapScalingMode.HighQuality);

        _ink = new InkCanvas { Background = Brushes.Transparent, Width = pw, Height = ph };
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (_suppress) return;
            PushUndo(new StrokeChange(_ink.Strokes, e.Added, e.Removed));
        };
        _ink.PreviewMouseLeftButtonDown += Ink_MouseDown;
        _ink.PreviewMouseMove += Ink_MouseMove;
        _ink.PreviewMouseLeftButtonUp += Ink_MouseUp;

        _textLayer = new Canvas { Width = pw, Height = ph, IsHitTestVisible = false };

        _surface = new Grid { Width = pw, Height = ph, ClipToBounds = true };
        _surface.Children.Add(imgCtl);
        _surface.Children.Add(_ink);
        _surface.Children.Add(_textLayer);

        // Capa de parpadeo: encima de la superficie pero fuera de ella,
        // para que no aparezca en la imagen exportada.
        _flash = new Border
        {
            Background = Brushes.White,
            Opacity = 0,
            IsHitTestVisible = false,
            BorderThickness = new Thickness(6),
        };
        _flash.SetResourceReference(Border.BorderBrushProperty, "Acento");
        var stack = new Grid();
        stack.Children.Add(_surface);
        stack.Children.Add(_flash);

        var host = new Border
        {
            Child = stack,
            LayoutTransform = _zoom,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.25 },
        };

        _scroll = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.SetResourceReference(BackgroundProperty, "Lienzo");
        _scroll.PreviewMouseWheel += (s, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetZoom(_zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
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
                    _ink.Strokes.Add(new StrokeCollection(fs));
                }
                catch { /* archivo dañado: se ignora */ }
            }

            if (File.Exists(_session.TextsPath))
            {
                try
                {
                    var notes = JsonSerializer.Deserialize<List<TextNote>>(File.ReadAllText(_session.TextsPath));
                    foreach (var n in notes ?? new List<TextNote>()) AddTextBlock(n);
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
                History.WriteAtomic(_session.InkPath, fs => _ink.Strokes.Save(fs));
            else if (File.Exists(_session.InkPath))
                File.Delete(_session.InkPath);

            var notes = _textLayer.Children.OfType<TextBlock>().Select(tb => new TextNote
            {
                Text = tb.Text,
                X = Canvas.GetLeft(tb),
                Y = Canvas.GetTop(tb),
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
        if (_undo.Count == 0) return;
        _suppress = true;
        try { _undo.Pop().Undo(); }
        finally { _suppress = false; }
        MarkChanged();
    }

    void ClearAll()
    {
        CommitText();
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
        UpdatePointer();
    }

    void FitZoom()
    {
        double availW = Math.Max(100, _scroll.ActualWidth - 60);
        double availH = Math.Max(100, _scroll.ActualHeight - 60);
        double fit = Math.Min(availW / _image.PixelWidth, availH / _image.PixelHeight);
        // Nunca agrandar por encima del tamaño real en pantalla
        SetZoom(Math.Min(1 / DpiScale(), fit));
    }

    // ------------------------------------------------------------------ teclado
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing != null) return; // escribiendo texto

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
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }
}
