// ----------------------------------------------------------------------------
//  Ventana de historial
// ----------------------------------------------------------------------------
sealed class HistoryWindow : Window
{
    readonly Action<Session> _open;
    readonly WrapPanel _panel = new WrapPanel { Margin = new Thickness(10) };
    readonly TextBlock _count = new TextBlock
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
    };

    public HistoryWindow(Action<Session> open)
    {
        _open = open;
        Title = $"Historial — {AppInfo.Name}";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Attach(this);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = Ui.Btn("Actualizar", "Recargar la lista", Refresh, Icons.Refresh);
        refresh.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(refresh);
        buttons.Children.Add(Ui.Btn("Abrir carpeta", "Abrir la carpeta del historial en el Explorador", () =>
        {
            History.EnsureRoot();
            Process.Start("explorer.exe", $"\"{History.Root}\"");
        }, Icons.Folder));

        var top = new DockPanel { Margin = new Thickness(16, 10, 12, 10) };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(_count);

        var root = new DockPanel();
        var topBorder = new Border
        {
            Child = top,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        topBorder.SetResourceReference(Border.BorderBrushProperty, "Borde");
        DockPanel.SetDock(topBorder, Dock.Top);
        root.Children.Add(topBorder);
        root.Children.Add(new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        // Se recarga cada vez que la ventana vuelve al frente
        Activated += (s, e) => Refresh();
    }

    void Refresh()
    {
        _panel.Children.Clear();
        var sessions = History.List();
        _count.Text = sessions.Count == 1 ? "1 captura" : $"{sessions.Count} capturas";

        if (sessions.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Todavía no hay capturas. Toma una con Impr Pant o Win+Shift+S.",
                Margin = new Thickness(10),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
            _panel.Children.Add(empty);
            return;
        }
        foreach (var s in sessions) _panel.Children.Add(Card(s));
    }

    Border Card(Session s)
    {
        var thumb = new Image { Height = 150, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) };
        try { if (File.Exists(s.ThumbPath)) thumb.Source = History.LoadImage(s.ThumbPath); } catch { }

        bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
        var info = new TextBlock
        {
            Text = $"{s.Meta.Updated:dd/MM/yyyy HH:mm}   ·   {s.Meta.Width}×{s.Meta.Height}{(abierta ? "   ·   abierta" : "")}",
            FontSize = 12,
        };
        info.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var resume = Ui.Btn("Retomar", "Abrir para seguir editando", () => _open(s), Icons.Pen, "EstiloPrimario");
        resume.Margin = new Thickness(0, 0, 8, 0);
        bar.Children.Add(resume);
        bar.Children.Add(Ui.Btn("Eliminar", "Eliminar del historial", () => Delete(s), Icons.Trash));

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(info);
        stack.Children.Add(bar);

        var card = new Border
        {
            Width = 270,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = "Doble clic para retomar",
        };
        card.SetResourceReference(Border.BackgroundProperty, "Superficie");
        card.SetResourceReference(Border.BorderBrushProperty, "Borde");
        card.MouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) _open(s); };
        return card;
    }

    void Delete(Session s)
    {
        if (EditorWindow.Open.TryGetValue(s.Meta.Id, out var w))
        {
            MessageBox.Show(this, "Esta captura está abierta. Ciérrala antes de eliminarla.", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Information);
            w.Activate();
            return;
        }
        if (MessageBox.Show(this, "¿Eliminar esta captura y sus anotaciones del historial?", AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try { History.Delete(s); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo eliminar", MessageBoxButton.OK, MessageBoxImage.Error); }
        Refresh();
    }
}
