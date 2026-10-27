// ----------------------------------------------------------------------------
//  Ventana de ajustes
//
//  Cada opción se guarda al tocarla (ajustes.json); no hay botón "Guardar".
//  Las opciones son grupos de botones segmentados, con los mismos estilos que
//  la barra del editor, para que respeten el tema claro/oscuro.
// ----------------------------------------------------------------------------
sealed class SettingsWindow : Window
{
    static SettingsWindow _open;

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

    readonly StackPanel _body = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };

    SettingsWindow()
    {
        Title = $"Ajustes — {AppInfo.Name}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Attach(this);

        var s = AppSettings.Current;

        Section("Apariencia");
        Choice("Tema", new[] { ("Claro", ModoTema.Claro), ("Oscuro", ModoTema.Oscuro), ("Como Windows", ModoTema.Sistema) },
            s.Tema, v => Theme.SetMode(v));

        Section("Editor");
        Choice("Numerar todo en capturas nuevas", new[] { ("Sí", true), ("No", false) },
            s.NumerarPorDefecto, v => Save(() => s.NumerarPorDefecto = v),
            "Apagado, solo se numera lo que tiene idea. Cada captura puede cambiarlo con el botón «123» de la barra.");
        Choice("Cambiar de color después de cada trazo", new[] { ("Sí", true), ("No", false) },
            s.RotarColores, v => Save(() => s.RotarColores = v),
            "Rojo → azul → verde → morado → naranja. El resaltador: amarillo → verde → azul.");
        Choice("Tamaño de los números", new[] { ("Normal", 1.0), ("Grande", 1.5), ("Muy grande", 2.0), ("Enorme", 2.6) },
            s.TamanoNumeros, v => Save(() => s.TamanoNumeros = v));
        Choice("Lo recién dibujado queda seleccionado", new[] { ("No", 0), ("2 s", 2), ("3 s", 3), ("5 s", 5), ("10 s", 10) },
            s.SegundosSeleccion, v => Save(() => s.SegundosSeleccion = v),
            "Mientras está seleccionado, elegir color o grosor se le aplica.");
        Choice("Grosor inicial", new[] { ("Fino", 2.0), ("Medio", 4.0), ("Grueso", 8.0) },
            s.GrosorInicial, v => Save(() => s.GrosorInicial = v));
        Choice("Cerrar el editor al copiar", new[] { ("Sí", true), ("No", false) },
            s.CerrarAlCopiar, v => Save(() => s.CerrarAlCopiar = v),
            "Con Ctrl+C, el clic derecho o el botón. Ctrl+Enter copia y cierra siempre.");
        FolderRow();

        Section("Historial");
        HotkeyRow();
        Choice("Conservar las últimas", AppSettings.MaxCapturasOpciones.Select(n => (n > 0 ? n.ToString() : "Todas", n)).ToArray(),
            s.MaxCapturas, v => { Save(() => s.MaxCapturas = v); CaptureHost.PruneHistory(); },
            "Las más viejas se eliminan solas. Las capturas abiertas nunca se eliminan.");
        Action<bool> selectStartup = null;
        selectStartup = Choice("Iniciar con Windows", new[] { ("Sí", true), ("No", false) },
            StartupHelper.IsEnabled(), v =>
            {
                // En el MSIX, Windows puede no dejar encenderlo: se muestra cómo quedó
                if (StartupHelper.Set(v) == v) return;
                selectStartup(!v);
                ConfirmDialog.Inform(this, "Iniciar con Windows", StartupHelper.BlockedMessage);
            });

        var folder = Ui.Btn("Abrir carpeta del historial", "Abrir en el Explorador", () =>
        {
            History.EnsureRoot();
            Process.Start("explorer.exe", $"\"{History.Root}\"");
        }, Icons.Folder);
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        folder.Margin = new Thickness(0, 8, 0, 0);
        _body.Children.Add(folder);

        var close = Ui.Btn("Cerrar", "Los cambios ya están guardados", Close, style: "EstiloPrimario");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 20, 0, 0);
        _body.Children.Add(close);

        Content = _body;
        PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape) Close(); };
    }

    static void Save(Action change)
    {
        change();
        try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }
    }

    void Section(string title)
    {
        var t = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, _body.Children.Count == 0 ? 0 : 18, 0, 6),
        };
        _body.Children.Add(t);
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
        _body.Children.Add(row);

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
        _body.Children.Add(row);
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
        _body.Children.Add(row);
    }
}
