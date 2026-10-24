// ----------------------------------------------------------------------------
//  Editor — panel de capturas recientes
//
//  Tira de miniaturas sobre la barra de estado. Arranca oculto para no quitarle
//  espacio a la captura; se abre con el botón de la barra o Ctrl+H, y mostrarlo u
//  ocultarlo vale para todas las pestañas (hasta que se cierra Popete).
//  Cada miniatura tiene una × para eliminarla; arriba, "Eliminar todas" y el
//  límite de capturas que se conservan (las más viejas se eliminan solas).
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Mostrado u oculto para toda la ventana: cada pestaña lo aplica al pasar al frente (OnSelected).</summary>
    static bool _recentsShown;

    Border _recents;
    StackPanel _recentsList;
    Button _keepButton;

    FrameworkElement BuildRecents()
    {
        var title = new TextBlock { Text = "Capturas recientes", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var close = Ui.IconBtn(Icons.Close, "Ocultar (Ctrl+H)", () => ToggleRecents(false));
        close.Width = close.Height = 28;

        _keepButton = Ui.Btn("", "Cuántas capturas se conservan; las más viejas se eliminan solas", () => ShowKeepMenu(), style: "EstiloPlano");
        UpdateKeepButton();
        var deleteAll = Ui.Btn("Eliminar todas", "Eliminar del historial todas las capturas que no estén abiertas",
            DeleteAllRecents, Icons.Trash, "EstiloPlano");
        deleteAll.Margin = new Thickness(4, 0, 8, 0);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_keepButton);
        actions.Children.Add(deleteAll);
        actions.Children.Add(close);

        var header = new DockPanel { Margin = new Thickness(12, 6, 8, 6) };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(title);

        _recentsList = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 10) };
        var scroll = new ScrollViewer
        {
            Content = _recentsList,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // La rueda desplaza la tira en horizontal
        scroll.PreviewMouseWheel += (s, e) =>
        {
            scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        };

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(scroll);

        _recents = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _recents.SetResourceReference(Border.BorderBrushProperty, "Borde");
        return _recents;
    }

    void ToggleRecents(bool? show = null)
    {
        bool on = show ?? _recents.Visibility != Visibility.Visible;
        _recentsShown = on;
        _recents.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _recentsButton.IsChecked = on;
        if (on) LoadRecents();
    }

    void LoadRecents()
    {
        _recentsList.Children.Clear();
        var sessions = History.List();
        foreach (var s in sessions) _recentsList.Children.Add(RecentCard(s));

        if (sessions.All(s => s.Meta.Id == _session.Meta.Id))
        {
            var empty = new TextBlock
            {
                Text = "Todavía no hay otras capturas en el historial.",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            _recentsList.Children.Add(empty);
        }
    }

    FrameworkElement RecentCard(Session s)
    {
        bool actual = s.Meta.Id == _session.Meta.Id;

        var img = new Image { Width = 148, Height = 84, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try { if (File.Exists(s.ThumbPath)) img.Source = History.LoadImage(s.ThumbPath); } catch { }

        bool abierta = !actual && EditorShell.IsOpen(s.Meta.Id);
        var label = new TextBlock
        {
            Text = actual ? "Esta captura" : $"{s.Meta.Updated:dd/MM HH:mm}{(abierta ? " · abierta" : "")}",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, actual ? "Acento" : "TextoSecundario");

        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(label);

        var b = new Button
        {
            Content = stack,
            ToolTip = actual ? "Es la captura que estás editando" : "Abrir esta captura",
        };
        b.SetResourceReference(StyleProperty, "EstiloMiniatura");
        if (actual)
        {
            b.BorderThickness = new Thickness(2);
            b.SetResourceReference(BorderBrushProperty, "Acento");
        }
        b.Click += (o, e) => { if (!actual) CaptureHost.OpenEditor(s); };

        var card = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        card.Children.Add(b);

        // × para eliminarla; no en las que están abiertas (esta u otro editor)
        if (!actual && !abierta)
        {
            var x = Ui.IconBtn(Icons.Close, "Eliminar del historial", () => DeleteRecent(s));
            x.Width = x.Height = 24;
            x.Margin = new Thickness(0, 4, 4, 0);
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.VerticalAlignment = VerticalAlignment.Top;
            x.SetResourceReference(BackgroundProperty, "Superficie");
            card.Children.Add(x);
        }
        return card;
    }

    void DeleteRecent(Session s)
    {
        try { History.Delete(s); }
        catch (Exception ex) { _status.Text = "No se pudo eliminar: " + ex.Message; }
        LoadRecents();
    }

    void DeleteAllRecents()
    {
        var deletable = History.List().Where(s => !EditorShell.Keeps(s.Meta.Id)).ToList();
        if (deletable.Count == 0) return;
        var title = deletable.Count == 1 ? "¿Eliminar 1 captura?" : $"¿Eliminar {deletable.Count} capturas?";
        if (!ConfirmDialog.Ask(_shell, title,
                "Se borran del historial con sus anotaciones, y no se puede deshacer. Las que están abiertas se conservan.",
                "Eliminar"))
            return;

        int failed = 0;
        foreach (var s in deletable)
        {
            try { History.Delete(s); }
            catch { failed++; }
        }
        if (failed > 0) _status.Text = $"No se pudieron eliminar {failed} capturas.";
        LoadRecents();
    }

    void UpdateKeepButton()
    {
        int max = AppSettings.Current.MaxCapturas;
        _keepButton.Content = max > 0 ? $"Conservar las últimas {max} ▾" : "Conservar todas ▾";
    }

    void ShowKeepMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _keepButton, Placement = PlacementMode.Bottom };
        foreach (int option in AppSettings.MaxCapturasOpciones)
        {
            var item = new MenuItem
            {
                Header = option > 0 ? $"Las últimas {option}" : "Todas (sin límite)",
                IsCheckable = true,
                IsChecked = AppSettings.Current.MaxCapturas == option,
            };
            int value = option;
            item.Click += (s, e) =>
            {
                AppSettings.Current.MaxCapturas = value;
                try { AppSettings.Current.Save(); } catch { /* se aplica igual en esta sesión */ }
                UpdateKeepButton();
                CaptureHost.PruneHistory();
                LoadRecents();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
