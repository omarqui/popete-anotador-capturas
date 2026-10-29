// ----------------------------------------------------------------------------
//  Ventana de ajustes
//
//  Cada opción se guarda al tocarla (ajustes.json); no hay botón "Guardar".
//  Un menú a la izquierda lleva a cada sección (Ctrl+Tab también); la última
//  es «Acerca de». Cada opción es una tarjeta; las elecciones son grupos de
//  botones segmentados, con los mismos estilos que la barra del editor, para
//  que respeten el tema claro/oscuro.
// ----------------------------------------------------------------------------
sealed class SettingsWindow : Window
{
    static SettingsWindow _open;
    static int _lastPage; // vuelve a abrir en la sección de la vez anterior (en esta sesión)

    /// <summary>Abre la ventana (una sola a la vez). <paramref name="owner"/> puede ser null (desde la bandeja).</summary>
    public static void Show(Window owner)
    {
        if (_open == null)
        {
            _open = new SettingsWindow { Owner = owner };
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }
        _open.Activate();
    }

    StackPanel _body; // la sección que se está armando: ahí van las filas
    readonly List<(RadioButton item, FrameworkElement page)> _pages = new();
    readonly StackPanel _nav = new StackPanel { Margin = new Thickness(12, 16, 12, 16) };
    readonly ScrollViewer _content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    SettingsWindow()
    {
        Title = $"Ajustes — {AppInfo.Name}";
        Width = 800;
        Height = 580;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Attach(this);

        var s = AppSettings.Current;

        Page("General", Icons.Settings, "Apariencia y arranque de Popete.");
        Choice("Modo de trabajo", new[] { ("En pestañas", ModoTrabajo.Pestanas), ("En el lugar", ModoTrabajo.EnElLugar) },
            s.ModoTrabajo, v => Save(() => s.ModoTrabajo = v),
            CaptureHost.SnipHookFailed
                ? "No se pudo tomar Win+Shift+S: sigue abriendo Recortes de Windows y la captura se abre en una pestaña."
                : "En el lugar: Win+Shift+S abre la selección de Popete y la captura se anota donde estaba, sobre la pantalla. "
                  + "En pestañas: Win+Shift+S es de Recortes de Windows y la captura se abre en el editor.");
        Choice("Tema", new[] { ("Claro", ModoTema.Claro), ("Oscuro", ModoTema.Oscuro), ("Como Windows", ModoTema.Sistema) },
            s.Tema, v => Theme.SetMode(v));
        Action<bool> selectStartup = null;
        selectStartup = Choice("Iniciar con Windows", new[] { ("Sí", true), ("No", false) },
            StartupHelper.IsEnabled(), v =>
            {
                // En el MSIX, Windows puede no dejar encenderlo: se muestra cómo quedó
                if (StartupHelper.Set(v) == v) return;
                selectStartup(!v);
                ConfirmDialog.Inform(this, "Iniciar con Windows", StartupHelper.BlockedMessage);
            }, "Popete espera en la bandeja y abre el editor con cada captura.");

        Page("Dibujo", Icons.Pen, "Cómo empiezan y se comportan los trazos en el editor.");
        Choice("Grosor inicial", new[] { ("Fino", 2.0), ("Medio", 4.0), ("Grueso", 8.0) },
            s.GrosorInicial, v => Save(() => s.GrosorInicial = v));
        Choice("Cambiar de color después de cada trazo", new[] { ("Sí", true), ("No", false) },
            s.RotarColores, v => Save(() => s.RotarColores = v),
            "Rojo → azul → verde → morado → naranja. El resaltador: amarillo → verde → azul.");
        Choice("Lo recién dibujado queda seleccionado", new[] { ("No", 0), ("2 s", 2), ("3 s", 3), ("5 s", 5), ("10 s", 10) },
            s.SegundosSeleccion, v => Save(() => s.SegundosSeleccion = v),
            "Mientras está seleccionado, elegir color o grosor se le aplica.");

        Page("Ideas", Icons.Idea, "Las notas de cada número: no salen en la imagen y se copian junto a ella.");
        IdeasHeaderRow();
        Choice("Llevan número", new[] { ("Los que tienen idea", false), ("Todos", true) },
            s.NumerarSiempre, v => Save(() => s.NumerarSiempre = v),
            "Todos: cada trazo y texto, con idea o sin ella. Las flechas que salen de un trazo nunca llevan número.");
        Choice("Tamaño de los números", new[] { ("Normal", 1.0), ("Grande", 1.5), ("Muy grande", 2.0), ("Enorme", 2.6) },
            s.TamanoNumeros, v => Save(() => s.TamanoNumeros = v));

        Page("Copiar y guardar", Icons.Copy, "Qué pasa al terminar con una captura.");
        Choice("Cerrar el editor al copiar", new[] { ("Sí", true), ("No", false) },
            s.CerrarAlCopiar, v => Save(() => s.CerrarAlCopiar = v),
            "Con Ctrl+C, el clic derecho o el botón. Ctrl+Enter copia y cierra siempre.");
        FolderRow();

        Page("Historial", Icons.Recents, "Las capturas que Popete guarda para retomarlas.");
        HotkeyRow();
        Choice("Conservar las últimas", AppSettings.MaxCapturasOpciones.Select(n => (n > 0 ? n.ToString() : "Todas", n)).ToArray(),
            s.MaxCapturas, v => { Save(() => s.MaxCapturas = v); CaptureHost.PruneHistory(); },
            "Las más viejas se eliminan solas. Las capturas abiertas nunca se eliminan.");
        ButtonRow("Carpeta del historial", History.Root, Ui.Btn("Abrir", "Abrir en el Explorador", () =>
        {
            History.EnsureRoot();
            Process.Start("explorer.exe", $"\"{History.Root}\"");
        }, Icons.Folder));

        Page("Actualizaciones", Icons.Refresh, "Las versiones nuevas llegan desde la Microsoft Store.");
        UpdatesPage();

        Page("Acerca de", Icons.Info, null);
        AboutPage();

        // Menú a la izquierda, sección a la derecha
        var navBorder = new Border { Width = 210, BorderThickness = new Thickness(0, 0, 1, 0), Child = _nav };
        navBorder.SetResourceReference(Border.BorderBrushProperty, "Borde");
        var root = new DockPanel();
        DockPanel.SetDock(navBorder, Dock.Left);
        root.Children.Add(navBorder);
        root.Children.Add(_content);
        Content = root;

        ShowPage(Math.Clamp(_lastPage, 0, _pages.Count - 1));
        PreviewKeyDown += (o, e) =>
        {
            if (e.Handled) return; // grabando el atajo
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                ShowPage((_lastPage + step + _pages.Count) % _pages.Count);
                e.Handled = true;
            }
        };
    }

    static void Save(Action change)
    {
        change();
        try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }
    }

    /// <summary>Empieza una sección: su entrada en el menú y su página, donde irán las filas que siguen.</summary>
    void Page(string title, string icon, string description)
    {
        _body = new StackPanel { Margin = new Thickness(28, 22, 28, 24), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Stretch };
        _body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        if (description != null)
        {
            var d = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            d.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            _body.Children.Add(d);
        }
        _body.Children.Add(new Border { Height = 14 });

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(Ui.Icon(icon, 18));
        label.Children.Add(new TextBlock { Text = title, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var item = new RadioButton { Content = label, GroupName = "secciones" };
        item.SetResourceReference(StyleProperty, "EstiloNavegacion");
        int index = _pages.Count;
        item.Checked += (o, e) => ShowPage(index);
        _nav.Children.Add(item);
        _pages.Add((item, _body));
    }

    void ShowPage(int index)
    {
        _lastPage = index;
        var (item, page) = _pages[index];
        item.IsChecked = true;
        if (_content.Content != page)
        {
            _content.Content = page;
            _content.ScrollToTop();
        }
    }

    /// <summary>Una opción en su tarjeta (fondo de superficie y borde, como la Configuración de Windows).</summary>
    void AddRow(UIElement row)
    {
        var card = new Border
        {
            Child = row,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Superficie");
        card.SetResourceReference(Border.BorderBrushProperty, "Borde");
        _body.Children.Add(card);
    }

    /// <summary>Fila con etiqueta, nota opcional (recortada con «…» si no entra) y un botón a la derecha.</summary>
    void ButtonRow(string label, string note, Button button)
    {
        button.VerticalAlignment = VerticalAlignment.Center;
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        labels.Children.Add(new TextBlock { Text = label });
        if (note != null)
        {
            var n = new TextBlock { Text = note, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = note, Margin = new Thickness(0, 2, 0, 0) };
            n.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            labels.Children.Add(n);
        }
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(labels);
        AddRow(row);
    }

    // ------------------------------------------------------------------ actualizaciones
    /// <summary>
    /// Estado de UpdateChecker (al día, versión nueva, buscando, sin conexión) con su botón, que
    /// se rehace en cada cambio; si buscar sola, y la página de la Store. Sin el MSIX lo explica.
    /// </summary>
    void UpdatesPage()
    {
        var s = AppSettings.Current;
        var glyph = new ContentControl { Width = 32, Height = 32, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var action = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };

        void Refresh()
        {
            var checker = UpdateChecker.Current;
            var state = checker?.State ?? (AppInfo.IsPackaged ? UpdateState.NotChecked : UpdateState.NotFromStore);
            string last = checker?.LastCheck is DateTime t
                ? " · Última revisión: " + (t.Date == DateTime.Today ? "hoy" : t.ToString("d MMM")) + $" a las {t:HH:mm}"
                : "";
            string installed = $"Versión instalada: {AppInfo.VersionText}";
            (string icon, string brush, string head, string body) = state switch
            {
                UpdateState.NotFromStore => (Icons.Info, "TextoSecundario", "Esta copia no viene de la Microsoft Store",
                    $"{installed}. Se actualiza compilándola de nuevo desde el código."),
                UpdateState.Checking => (Icons.Refresh, "Acento", "Buscando actualizaciones…", installed),
                UpdateState.UpToDate => (Icons.Check, "Exito", "Popete está al día", installed + last),
                UpdateState.Available => (Icons.Save, "Acento", $"Hay una versión nueva: {checker.AvailableVersion}",
                    $"{installed}. Al actualizar, Popete se cierra y se vuelve a abrir solo; las capturas abiertas quedan guardadas."),
                UpdateState.Installing => (Icons.Save, "Acento", "Descargando la versión nueva…", "Popete se va a cerrar y volver a abrir."),
                UpdateState.Failed => (Icons.Info, "Error", "No se pudo consultar la Microsoft Store",
                    "Revisa la conexión a internet e intenta de nuevo." + last),
                _ => (Icons.Refresh, "TextoSecundario", "Todavía no se buscaron actualizaciones", installed),
            };
            var iconShape = Ui.Icon(icon, 32, 2);
            iconShape.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brush);
            glyph.Content = iconShape;
            title.Text = head;
            detail.Text = body;

            action.Content = state switch
            {
                UpdateState.Available => Ui.Btn("Actualizar ahora", "Descargar e instalar la versión nueva", () => checker.Install(), style: "EstiloPrimario"),
                UpdateState.NotChecked or UpdateState.UpToDate or UpdateState.Failed =>
                    Ui.Btn("Buscar actualizaciones", "Preguntarle ahora a la Microsoft Store", () => _ = checker?.CheckNow(), Icons.Refresh),
                _ => null,
            };
        }

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(detail);
        var status = new DockPanel { Margin = new Thickness(0, 10, 0, 10) };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(action, Dock.Right);
        status.Children.Add(glyph);
        status.Children.Add(action);
        status.Children.Add(texts);
        AddRow(status);

        Action onChanged = Refresh;
        UpdateChecker.Changed += onChanged;
        Closed += (o, e) => UpdateChecker.Changed -= onChanged;
        Refresh();

        if (!AppInfo.IsPackaged) return;
        Choice("Buscar actualizaciones automáticamente", new[] { ("Sí", true), ("No", false) },
            s.BuscarActualizaciones, v => Save(() => s.BuscarActualizaciones = v),
            "Al rato de abrir Popete y cada 6 horas. Si hay una versión nueva, avisa en la bandeja.");
        ButtonRow("Microsoft Store", "Novedades y reseñas de Popete",
            Ui.Btn("Abrir", "La página de Popete en la Microsoft Store", UpdateChecker.OpenStorePage, Icons.Link));
    }

    // ------------------------------------------------------------------ acerca de
    void AboutPage()
    {
        // Cabecera: ícono, nombre, subtítulo y versión
        // 64 DIP: con escala de pantalla de hasta 200 %, el cuadro de 128 px o el de 256 px
        var icon = new Image { Source = AppInfo.LargeIcon(128), Width = 64, Height = 64, Margin = new Thickness(0, 0, 18, 0) };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 26, FontWeight = FontWeights.SemiBold });
        var sub = new TextBlock { Text = $"{char.ToUpper(AppInfo.Subtitle[0])}{AppInfo.Subtitle[1..]} para Windows" };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        names.Children.Add(sub);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(icon);
        header.Children.Add(names);
        _body.Children.Insert(1, header); // debajo del título «Acerca de»

        string version = AppInfo.VersionText;
        string install = AppInfo.IsPackaged ? "Microsoft Store" : "Sin instalar (compilada desde el código)";
        string runtime = $".NET {Environment.Version}";
        string windows = $"Windows {Environment.OSVersion.Version}";

        InfoRow("Versión", version);
        InfoRow("Autor", AppInfo.Author);
        LinkRow("Contacto", AppInfo.Email, "mailto:" + AppInfo.Email);
        InfoRow("Instalación", install);
        InfoRow("Sistema", $"{windows} · {runtime}");
        ButtonRow("Datos de Popete", AppInfo.DataRoot, Ui.Btn("Abrir", "Abrir en el Explorador", () =>
        {
            Directory.CreateDirectory(AppInfo.DataRoot);
            Process.Start("explorer.exe", $"\"{AppInfo.DataRoot}\"");
        }, Icons.Folder));

        // Acciones: copiar los datos para reportar un problema y ver las novedades (WhatsNewWindow)
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        Button copy = null;
        copy = Ui.Btn("Copiar información", "Versión y sistema, para reportar un problema", () =>
        {
            var text = $"{AppInfo.Name} {version} · {install} · {windows} · {runtime}";
            copy.Content = Ui.Label(ClipboardHelper.SetText(text) ? "Copiada" : "No se pudo copiar", Icons.Check);
        }, Icons.Copy);
        actions.Children.Add(copy);
        actions.Children.Add(Ui.Btn("Novedades", "Lo nuevo de esta versión", () => WhatsNewWindow.Show(this), Icons.Info));
        _body.Children.Add(actions);

        var copyright = new TextBlock { Text = $"© {DateTime.Now.Year} {AppInfo.Author}", FontSize = 12, Margin = new Thickness(0, 20, 0, 0) };
        copyright.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        _body.Children.Add(copyright);
    }

    /// <summary>Fila de dato: etiqueta a la izquierda y valor (seleccionable) a la derecha.</summary>
    void InfoRow(string label, string value) => AddRow(DataRow(label, new TextBox
    {
        Text = value,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        TextAlignment = TextAlignment.Right,
        Padding = new Thickness(0),
    }));

    void LinkRow(string label, string text, string url)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { ToolTip = url };
        link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Acento");
        link.Click += (o, e) => Open(url);
        AddRow(DataRow(label, new TextBlock(link) { TextAlignment = TextAlignment.Right }));
    }

    static DockPanel DataRow(string label, FrameworkElement value)
    {
        if (value is TextBox box)
        {
            box.SetResourceReference(ForegroundProperty, "Texto");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Texto");
        }
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(16, 0, 0, 0);
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(name, Dock.Left);
        row.Children.Add(name);
        row.Children.Add(value);
        return row;
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* sin navegador o cliente de correo: no hay nada que hacer */ }
    }

    /// <summary>Una fila: etiqueta (y nota opcional) a la izquierda, opciones segmentadas a la derecha.</summary>
    /// <returns>Marca otra opción sin aplicarla (para mostrar el valor real si el cambio no se pudo hacer).</returns>
    Action<T> Choice<T>(string label, (string text, T value)[] options, T current, Action<T> apply, string note = null)
    {
        var buttons = new List<ToggleButton>();
        foreach (var (text, value) in options)
        {
            var b = new ToggleButton
            {
                Content = new TextBlock { Text = text, Margin = new Thickness(10, 0, 10, 0), FontSize = 12 },
                Width = double.NaN,
                MinWidth = 44,
                IsChecked = EqualityComparer<T>.Default.Equals(value, current),
            };
            b.SetResourceReference(StyleProperty, "EstiloSegmento");
            var v = value;
            b.Click += (s, e) =>
            {
                foreach (var other in buttons) other.IsChecked = other == b;
                apply(v);
            };
            buttons.Add(b);
        }
        var group = Ui.Group(buttons.ToArray());
        group.VerticalAlignment = VerticalAlignment.Center;

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        labels.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (note != null)
        {
            var n = new TextBlock { Text = note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            n.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            labels.Children.Add(n);
        }

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(group, Dock.Right);
        row.Children.Add(group);
        row.Children.Add(labels);
        AddRow(row);

        return value =>
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].IsChecked = EqualityComparer<T>.Default.Equals(options[i].value, value);
        };
    }

    /// <summary>
    /// Atajo global del historial rápido: el botón graba la combinación que se apriete (Esc
    /// cancela) y "Quitar" lo deja sin atajo. Mientras graba, el atajo actual queda en pausa
    /// para que no abra el historial. Si otro programa ya usa la combinación, lo avisa.
    /// </summary>
    void HotkeyRow()
    {
        const string Help = "Abre las capturas desde cualquier lugar, aunque Popete esté solo en la bandeja. "
            + "Mantén los modificadores y repite la tecla para bajar; al soltar se abre la elegida.";
        var s = AppSettings.Current;
        bool recording = false;

        var note = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var record = new Button { MinWidth = 120, ToolTip = "Cambiar la combinación" };
        record.SetResourceReference(StyleProperty, "EstiloSecundario");
        Button clear = null; // Refresh lo usa: tiene que estar asignado antes de la lambda que lo llama
        clear = Ui.Btn("Quitar", "Dejar el historial rápido sin atajo", () =>
        {
            Save(() => s.AtajoHistorial = null);
            Refresh();
        }, style: "EstiloPlano");
        clear.Margin = new Thickness(4, 0, 0, 0);

        void Refresh(string message = null, bool error = false)
        {
            record.Content = recording ? "Presiona la combinación…" : s.AtajoHistorial?.Text ?? "Sin atajo";
            clear.Visibility = !recording && s.AtajoHistorial != null ? Visibility.Visible : Visibility.Collapsed;
            if (message == null && !recording && CaptureHost.HotkeyFailed)
            {
                message = "Otro programa ya usa esta combinación: elige otra.";
                error = true;
            }
            note.Text = message ?? (recording ? "Usa Ctrl o Alt, con Shift si quieres. Esc cancela." : Help);
            note.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "TextoSecundario");
        }

        void Finish(Hotkey chosen)
        {
            if (!recording) return;
            recording = false;
            if (chosen != null) Save(() => s.AtajoHistorial = chosen);
            CaptureHost.PauseHotkey(false); // registra el de los ajustes (y avisa si está ocupado)
            Refresh();
        }

        record.Click += (o, e) =>
        {
            recording = true;
            CaptureHost.PauseHotkey(true);
            Refresh();
        };
        PreviewKeyDown += (o, e) =>
        {
            if (!recording) return;
            e.Handled = true; // tampoco cierra la ventana con Esc
            var key = e.Key == Key.System ? e.SystemKey : e.Key; // con Alt apretado, WPF manda Key.System
            if (key == Key.Escape) Finish(null);
            else if (Hotkey.IsModifier(key)) return; // espera la tecla
            else if (new Hotkey(Keyboard.Modifiers, key) is { IsValid: true } chosen) Finish(chosen);
            else Refresh("Esa combinación no sirve: usa Ctrl o Alt (con Shift si quieres) y otra tecla. Esc cancela.", error: true);
        };
        // Al alt-tabular o cerrar a mitad de la grabación, se cancela
        Deactivated += (o, e) => Finish(null);
        Closed += (o, e) => Finish(null);
        Refresh();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(record);
        buttons.Children.Add(clear);

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        labels.Children.Add(new TextBlock { Text = "Atajo del historial rápido" });
        labels.Children.Add(note);

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(labels);
        AddRow(row);
    }

    /// <summary>Encabezado que va antes de la lista de ideas al copiar. Se guarda al salir del cuadro o al cerrar.</summary>
    void IdeasHeaderRow()
    {
        var s = AppSettings.Current;
        var box = new TextBox
        {
            Text = s.EncabezadoIdeas ?? "",
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
        };
        box.SetResourceReference(BackgroundProperty, "Superficie");
        box.SetResourceReference(ForegroundProperty, "Texto");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Texto");
        box.SetResourceReference(BorderBrushProperty, "Borde");
        void Apply()
        {
            if (box.Text != s.EncabezadoIdeas) Save(() => s.EncabezadoIdeas = box.Text);
        }
        box.LostKeyboardFocus += (o, e) => Apply();
        Closed += (o, e) => Apply();

        var note = new TextBlock { Text = "Va antes de la lista al copiar. Vacío: solo la lista.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");

        var row = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = "Encabezado de las ideas" });
        row.Children.Add(note);
        row.Children.Add(box);
        AddRow(row);
    }

    /// <summary>Carpeta que propone "Guardar como".</summary>
    void FolderRow()
    {
        var path = new TextBlock
        {
            Text = AppSettings.Current.CarpetaGuardarEfectiva,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        path.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");

        var change = Ui.Btn("Cambiar…", "Elegir la carpeta", () =>
        {
            var dlg = new OpenFolderDialog { InitialDirectory = AppSettings.Current.CarpetaGuardarEfectiva };
            if (dlg.ShowDialog(this) != true) return;
            Save(() => AppSettings.Current.CarpetaGuardar = dlg.FolderName);
            path.Text = AppSettings.Current.CarpetaGuardarEfectiva;
        });
        change.VerticalAlignment = VerticalAlignment.Center;

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        labels.Children.Add(new TextBlock { Text = "Carpeta para «Guardar»" });
        labels.Children.Add(path);

        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(change, Dock.Right);
        row.Children.Add(change);
        row.Children.Add(labels);
        AddRow(row);
    }
}
