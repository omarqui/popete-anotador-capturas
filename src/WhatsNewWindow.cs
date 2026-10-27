// ----------------------------------------------------------------------------
//  Novedades después de actualizar
//
//  Al arrancar el MSIX, si la versión instalada es más nueva que la última cuyas
//  novedades se vieron (AppSettings.UltimaVersionVista), un globo en la bandeja lo
//  avisa en lugar del de «Activo». La ventana sale al tocar el globo o, si no, la
//  primera vez que se abre el editor; recién entonces la versión cuenta como vista
//  (si no llegó a mostrarse, el globo vuelve en el próximo arranque). Una
//  instalación nueva no las muestra. Siempre se pueden ver en Ajustes › Acerca de.
//
//  El texto es el «What's new» de Partner Center, que se pide a la Store; si no
//  responde o trae el de otra versión, la copia de novedades.txt embebida en el
//  .exe (ReleaseNotes.Pick). Sin paquete, siempre la embebida.
// ----------------------------------------------------------------------------
sealed class WhatsNewWindow : Window
{
    static readonly TimeSpan StoreTimeout = TimeSpan.FromSeconds(5);

    static WhatsNewWindow _open;
    static System.Threading.Tasks.Task<string> _text; // se pide una vez por sesión
    static string _pending;      // versión cuyas novedades faltan mostrar
    static bool _balloonIsOurs;  // el clic en el globo es para ver las novedades (hay otros globos)

    /// <summary>
    /// Al arrancar: si Popete se actualizó, muestra el globo de las novedades y devuelve true (el de
    /// «Activo» sobra). Sin paquete no hace nada.
    /// </summary>
    public static bool OfferAfterUpdate(System.Windows.Forms.NotifyIcon tray)
    {
        if (!AppInfo.IsPackaged) return false;
        var installed = AppInfo.PackageVersion;
        var settings = AppSettings.Current;
        if (!ReleaseNotes.ShouldShow(installed, settings.UltimaVersionVista, AppSettings.HadFile))
        {
            // Instalación nueva (o vuelta a una versión anterior): solo se anota
            if (settings.UltimaVersionVista != installed)
            {
                settings.UltimaVersionVista = installed;
                settings.Save();
            }
            return false;
        }
        _pending = installed;
        _text = Load(); // la Store ya va respondiendo mientras tanto
        tray.BalloonTipClicked += (s, e) => { if (_balloonIsOurs) Show(null); };
        tray.BalloonTipClosed += (s, e) => _balloonIsOurs = false;
        _balloonIsOurs = true;
        tray.ShowBalloonTip(10000, $"Popete se actualizó a la versión {installed}",
            "Haz clic aquí para ver las novedades. También están en Ajustes › Acerca de.",
            System.Windows.Forms.ToolTipIcon.Info);
        return true;
    }

    /// <summary>Después de abrir el editor: si quedaron novedades sin ver, encima de él.</summary>
    public static void OnEditorShown(Window shell)
    {
        if (_pending != null) Show(shell);
    }

    /// <summary>Muestra las novedades (o trae al frente la ventana, si ya está abierta).</summary>
    public static async void Show(Window owner)
    {
        if (_open == null)
        {
            var text = await (_text ??= Load());
            if (_open == null)
            {
                _open = new WhatsNewWindow(owner, text);
                _open.Closed += (s, e) => _open = null;
                _open.Show();
            }
        }
        _open.Activate();
        if (_pending == null) return;
        AppSettings.Current.UltimaVersionVista = _pending;
        _pending = null;
        AppSettings.Current.Save();
    }

    static async System.Threading.Tasks.Task<string> Load()
    {
        var embedded = Embedded();
        if (!AppInfo.IsPackaged) return embedded;
        try
        {
            var query = Windows.Services.Store.StoreContext.GetDefault().GetStoreProductForCurrentAppAsync().AsTask();
            if (await System.Threading.Tasks.Task.WhenAny(query, System.Threading.Tasks.Task.Delay(StoreTimeout)) == query)
            {
                var json = (await query).Product?.ExtendedJsonData;
                return ReleaseNotes.Pick(ReleaseNotes.FromCatalogJson(json), embedded, AppInfo.PackageVersion);
            }
        }
        catch
        {
            // Sin conexión o un paquete que no vino de la Store: alcanza con la copia embebida
        }
        return embedded;
    }

    static string Embedded()
    {
        using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream("novedades.txt");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    WhatsNewWindow(Window owner, string text)
    {
        Title = $"Novedades de {AppInfo.Name}";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = owner == null;
        if (owner != null && owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Attach(this);

        // La primera línea («Novedades de la versión 1.2.0:») es el título; el resto, el texto
        var lines = text.Replace("\r\n", "\n").Trim().Split('\n');
        bool titled = ReleaseNotes.VersionOf(lines[0]) != null;
        var heading = new TextBlock
        {
            Text = titled ? lines[0].Trim().TrimEnd(':') : Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new TextBlock
        {
            Text = string.Join("\n", titled ? lines.Skip(1) : lines).Trim(),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var scroll = new ScrollViewer
        {
            Content = body,
            MaxHeight = 440,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        content.Children.Add(heading);
        content.Children.Add(scroll);

        var close = Ui.Btn("Cerrar", "", Close, style: "EstiloPrimario");
        close.Margin = new Thickness(0);
        close.MinWidth = 120;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = new Border
        {
            Child = close,
            Padding = new Thickness(24, 16, 24, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        footer.SetResourceReference(Border.BackgroundProperty, "Superficie");
        footer.SetResourceReference(Border.BorderBrushProperty, "Borde");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(content);
        Content = root;

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key is not (Key.Escape or Key.Enter)) return;
            e.Handled = true;
            Close();
        };
    }
}
