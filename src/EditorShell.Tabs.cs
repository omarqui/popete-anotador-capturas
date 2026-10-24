// ----------------------------------------------------------------------------
//  Ventana del editor — pestañas
//
//  Una cabecera por captura: miniatura, hora y ×. Clic la elige; la ×, el clic
//  central o Ctrl+W la cierran; arrastrarla la cambia de lugar (como los botones
//  de la barra de herramientas, EditorView.ToolOrder.cs) y el clic derecho abre
//  su menú. Con muchas, se achican hasta TabMinWidth y después la tira se
//  desplaza con la rueda. A la derecha siempre queda DragSpace libre para
//  arrastrar la ventana.
// ----------------------------------------------------------------------------
sealed partial class EditorShell
{
    const double TabMaxWidth = 200;
    const double TabMinWidth = 104;
    const double TabHeight = 32;
    const double TabGap = 2;
    const double DragSpace = 56;

    static readonly System.Globalization.CultureInfo Spanish = new System.Globalization.CultureInfo("es");

    readonly StackPanel _strip = new StackPanel { Orientation = Orientation.Horizontal };
    readonly ScrollViewer _stripScroll = new ScrollViewer
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Focusable = false,
    };
    readonly Grid _stripHost = new Grid();
    Border _drag;          // cabecera que se está arrastrando
    bool _dragging;
    double _dragDownX;
    double _dragGrab;      // dónde se la tomó, desde su borde izquierdo

    FrameworkElement BuildTabStrip()
    {
        _stripScroll.Content = _strip;
        _stripScroll.PreviewMouseWheel += (s, e) =>
        {
            _stripScroll.ScrollToHorizontalOffset(_stripScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        _stripHost.Children.Add(_stripScroll);
        _stripHost.SizeChanged += (s, e) => LayoutTabs();
        HookTabDrag();
        return _stripHost;
    }

    /// <summary>Todas del mismo ancho: lo que entre entre TabMinWidth y TabMaxWidth.</summary>
    void LayoutTabs()
    {
        double available = Math.Max(0, _stripHost.ActualWidth - DragSpace);
        _stripScroll.MaxWidth = available;
        if (_tabs.Count == 0) return;
        double width = Math.Clamp(available / _tabs.Count - TabGap, TabMinWidth, TabMaxWidth);
        foreach (var t in _tabs) t.Header.Width = width;
    }

    void BuildHeader(Tab tab)
    {
        var m = tab.Session.Meta;
        tab.Thumb = new Image { Width = 24, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(tab.Thumb, BitmapScalingMode.HighQuality);
        tab.Text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        tab.CloseButton = Ui.IconBtn(Icons.Close, "Cerrar (Ctrl+W)", () => CloseTab(tab));
        tab.CloseButton.Content = Ui.Icon(Icons.Close, 12, 2);
        tab.CloseButton.Width = tab.CloseButton.Height = 22;
        tab.CloseButton.Margin = new Thickness(4, 0, 0, 0);
        tab.CloseButton.VerticalAlignment = VerticalAlignment.Center;

        var dock = new DockPanel();
        DockPanel.SetDock(tab.Thumb, Dock.Left);
        DockPanel.SetDock(tab.CloseButton, Dock.Right);
        dock.Children.Add(tab.Thumb);
        dock.Children.Add(tab.CloseButton);
        dock.Children.Add(tab.Text);

        var header = new Border
        {
            Child = dock,
            Height = TabHeight,
            Padding = new Thickness(10, 0, 5, 0),
            Margin = new Thickness(0, 0, TabGap, 0),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            ToolTip = $"{Capitalize(m.Created.ToString("dddd d 'de' MMMM, HH:mm", Spanish))} · {m.Width}×{m.Height}",
            ContextMenu = new ContextMenu(),
            Tag = tab,
        };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(header, true);
        header.ContextMenuOpening += (s, e) => FillTabMenu(header.ContextMenu, tab);
        header.MouseUp += (s, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle) return;
            CloseTab(tab);
            e.Handled = true;
        };
        header.MouseEnter += (s, e) => PaintHeader(tab);
        header.MouseLeave += (s, e) => PaintHeader(tab);
        tab.Header = header;
        PaintHeader(tab);
        LoadThumb(tab);
    }

    /// <summary>La elegida tiene el fondo del editor (se funde con la barra de abajo); las demás, transparente.</summary>
    void PaintHeader(Tab tab)
    {
        bool selected = tab == _selected, hover = tab.Header.IsMouseOver;
        if (selected) tab.Header.SetResourceReference(Border.BackgroundProperty, "Fondo");
        else if (hover) tab.Header.SetResourceReference(Border.BackgroundProperty, "Hover");
        else tab.Header.Background = Brushes.Transparent;
        tab.Text.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Texto" : "TextoSecundario");
        tab.CloseButton.Visibility = selected || hover ? Visibility.Visible : Visibility.Hidden;
    }

    void RefreshHeaders()
    {
        foreach (var t in _tabs)
        {
            t.Text.Text = TabText(t.Session.Meta.Created); // "10:42" pasa a "Ayer 10:42" después de medianoche
            PaintHeader(t);
        }
    }

    void BringHeaderIntoView(Tab tab) =>
        Dispatcher.InvokeAsync(() => tab.Header.BringIntoView(), DispatcherPriority.Loaded);

    /// <summary>Miniatura de la pestaña, después de mostrarla (y otra vez cada vez que el editor guarda).</summary>
    void LoadThumb(Tab tab) =>
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (File.Exists(tab.Session.ThumbPath)) tab.Thumb.Source = History.LoadImage(tab.Session.ThumbPath, 48);
            }
            catch { /* miniatura dañada: la pestaña queda sin imagen */ }
        }, DispatcherPriority.Background);

    /// <summary>"10:42" (hoy), "Ayer 18:03", "Lun 09:12" (esta semana) o "12/10 08:55". No cambia con los minutos.</summary>
    static string TabText(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return t.ToString("HH:mm");
        if (t.Date == today.AddDays(-1)) return $"Ayer {t:HH:mm}";
        if (t.Date > today.AddDays(-7)) return Capitalize(t.ToString("ddd", Spanish).TrimEnd('.')) + t.ToString(" HH:mm");
        return t.ToString("dd/MM HH:mm");
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Spanish) + s[1..];

    // ------------------------------------------------------------------ menú del clic derecho
    void FillTabMenu(ContextMenu menu, Tab tab)
    {
        menu.Items.Clear();
        int index = _tabs.IndexOf(tab);
        menu.Items.Add(Item("Cerrar", () => CloseTab(tab), "Ctrl+W"));
        menu.Items.Add(Item("Cerrar las demás", () => CloseOthers(tab, _tabs.Where(t => t != tab).ToList()), enabled: _tabs.Count > 1));
        menu.Items.Add(Item("Cerrar las de la derecha", () => CloseOthers(tab, _tabs.Skip(index + 1).ToList()), enabled: index < _tabs.Count - 1));
        menu.Items.Add(Item("Cerrar todas", CloseAll));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Mostrar en el Explorador", () => ShowInExplorer(tab.Session)));
        menu.Items.Add(Item("Eliminar del historial…", () => DeleteTab(tab)));
    }

    static MenuItem Item(string header, Action action, string gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
        item.Click += (s, e) => action();
        return item;
    }

    /// <summary>Cierra <paramref name="victims"/>; si la elegida está entre ellas, antes pasa a <paramref name="keep"/>.</summary>
    void CloseOthers(Tab keep, List<Tab> victims)
    {
        if (victims.Contains(_selected)) Select(keep, byUser: true);
        foreach (var t in victims) CloseTab(t);
    }

    /// <summary>"Cerrar todas": cierra la ventana sin dejar pestañas para la próxima vez.</summary>
    void CloseAll()
    {
        _clearOnClose = true;
        Close();
    }

    static void ShowInExplorer(Session s)
    {
        var target = File.Exists(s.PreviewPath) ? s.PreviewPath : s.Folder;
        Process.Start("explorer.exe", $"/select,\"{target}\"");
    }

    void DeleteTab(Tab tab)
    {
        if (!ConfirmDialog.Ask(this, "¿Eliminar esta captura?",
                "Se cierra la pestaña y se borra del historial con sus anotaciones. No se puede deshacer.", "Eliminar"))
            return;
        var session = tab.Session;
        bool last = _tabs.Count == 1;
        CloseTab(tab); // guarda lo pendiente y suelta el editor antes de borrar la carpeta
        try { History.Delete(session); }
        catch (Exception ex) { ConfirmDialog.Inform(last ? null : this, "No se pudo eliminar", ex.Message); }
    }

    // ------------------------------------------------------------------ arrastrar para reordenar
    void HookTabDrag()
    {
        _strip.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _drag = null;
            _dragging = false;
            var header = HeaderOf(e.OriginalSource as DependencyObject);
            if (header == null || InButton(e.OriginalSource as DependencyObject, header)) return; // la × se maneja sola
            Select((Tab)header.Tag, byUser: true);
            _drag = header;
            _dragDownX = e.GetPosition(_strip).X;
            _dragGrab = _dragDownX - LayoutInformation.GetLayoutSlot(header).X;
        };
        _strip.PreviewMouseMove += (s, e) =>
        {
            if (_drag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndTabDrag();
                return;
            }
            double x = e.GetPosition(_strip).X;
            if (!_dragging)
            {
                if (Math.Abs(x - _dragDownX) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
                _dragging = true;
                _strip.CaptureMouse();
                Panel.SetZIndex(_drag, 1);
            }
            DragTabTo(x);
            e.Handled = true;
        };
        _strip.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_dragging) e.Handled = true;
            EndTabDrag();
        };
        // Solo cuando la tira pierde el mouse (ver nota 37 de AGENTS.md)
        _strip.LostMouseCapture += (s, e) =>
        {
            if (e.OriginalSource == _strip) EndTabDrag();
        };
    }

    /// <summary>La cabecera que contiene <paramref name="d"/>, o null.</summary>
    Border HeaderOf(DependencyObject d)
    {
        while (d != null)
        {
            var parent = VisualTreeHelper.GetParent(d);
            if (parent == _strip) return d as Border;
            d = parent;
        }
        return null;
    }

    static bool InButton(DependencyObject d, DependencyObject stop)
    {
        for (; d != null && d != stop; d = VisualTreeHelper.GetParent(d))
            if (d is ButtonBase) return true;
        return false;
    }

    /// <summary>La cabecera sigue al mouse; cuando su centro pasa el de una vecina, cambian de lugar.</summary>
    void DragTabTo(double x)
    {
        double width = LayoutInformation.GetLayoutSlot(_drag).Width;
        double left = Math.Clamp(x - _dragGrab, 0, Math.Max(0, _strip.ActualWidth - width));
        double center = left + width / 2;
        int index = _strip.Children.Cast<FrameworkElement>()
            .Where(c => c != _drag)
            .Count(c =>
            {
                var r = LayoutInformation.GetLayoutSlot(c);
                return r.X + r.Width / 2 < center;
            });
        if (_strip.Children.IndexOf(_drag) != index)
        {
            _strip.Children.Remove(_drag);
            _strip.Children.Insert(index, _drag);
            _strip.UpdateLayout();
        }
        _drag.RenderTransform = new TranslateTransform(left - LayoutInformation.GetLayoutSlot(_drag).X, 0);
    }

    /// <summary>Suelta la cabecera en su lugar y, si hubo arrastre, guarda el orden nuevo.</summary>
    void EndTabDrag()
    {
        var dragged = _drag;
        bool moved = _dragging;
        _drag = null;
        _dragging = false;
        if (dragged == null || !moved) return;
        dragged.RenderTransform = null;
        Panel.SetZIndex(dragged, 0);
        if (_strip.IsMouseCaptured) _strip.ReleaseMouseCapture();
        var order = _strip.Children.Cast<Border>().Select(h => (Tab)h.Tag).ToList();
        _tabs.Clear();
        _tabs.AddRange(order);
        SaveState();
    }
}
