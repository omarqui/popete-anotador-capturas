// ----------------------------------------------------------------------------
//  Editor — panel de capturas recientes
//
//  Tira de miniaturas sobre la barra de estado. Siempre arranca oculto para no
//  quitarle espacio a la captura; se abre con el botón de la barra o Ctrl+H.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    Border _recents;
    StackPanel _recentsList;

    FrameworkElement BuildRecents()
    {
        var title = new TextBlock { Text = "Capturas recientes", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var close = Ui.IconBtn(Icons.Close, "Ocultar (Ctrl+H)", () => ToggleRecents(false));
        close.Width = close.Height = 28;

        var header = new DockPanel { Margin = new Thickness(12, 6, 8, 6) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
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
        _recents.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _recentsButton.IsChecked = on;
        if (on) LoadRecents();
    }

    void LoadRecents()
    {
        _recentsList.Children.Clear();
        var sessions = History.List().Take(20).ToList();
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

        bool abierta = !actual && Open.ContainsKey(s.Meta.Id);
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
            Margin = new Thickness(0, 0, 8, 0),
        };
        b.SetResourceReference(StyleProperty, "EstiloMiniatura");
        if (actual)
        {
            b.BorderThickness = new Thickness(2);
            b.SetResourceReference(BorderBrushProperty, "Acento");
        }
        b.Click += (o, e) => { if (!actual) CaptureHost.OpenEditor(s); };
        return b;
    }
}
