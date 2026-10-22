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
        Title = "Historial de capturas";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Btn("🔄 Actualizar", "Recargar la lista", Refresh));
        buttons.Children.Add(Ui.Btn("📂 Abrir carpeta", "Abrir la carpeta del historial en el Explorador", () =>
        {
            History.EnsureRoot();
            Process.Start("explorer.exe", $"\"{History.Root}\"");
        }));

        var top = new DockPanel { Margin = new Thickness(14, 8, 10, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(_count);

        var root = new DockPanel();
        var topBorder = new Border
        {
            Child = top,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
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
            _panel.Children.Add(new TextBlock
            {
                Text = "Todavía no hay capturas. Toma una con Impr Pant o Win+Shift+S.",
                Margin = new Thickness(10),
                Foreground = Brushes.DimGray,
            });
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
            Foreground = Brushes.DimGray,
            FontSize = 12,
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(Ui.Btn("✏ Retomar", "Abrir para seguir editando", () => _open(s)));
        bar.Children.Add(Ui.Btn("🗑 Eliminar", "Eliminar del historial", () => Delete(s)));

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(info);
        stack.Children.Add(bar);

        var card = new Border
        {
            Width = 270,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = "Doble clic para retomar",
        };
        card.MouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) _open(s); };
        return card;
    }

    void Delete(Session s)
    {
        if (EditorWindow.Open.TryGetValue(s.Meta.Id, out var w))
        {
            MessageBox.Show(this, "Esta captura está abierta. Ciérrala antes de eliminarla.", "Historial de capturas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            w.Activate();
            return;
        }
        if (MessageBox.Show(this, "¿Eliminar esta captura y sus anotaciones del historial?", "Historial de capturas",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try { History.Delete(s); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo eliminar", MessageBoxButton.OK, MessageBoxImage.Error); }
        Refresh();
    }
}
