// ----------------------------------------------------------------------------
//  Buzón de sugerencias (bandeja › «Enviar una sugerencia…» y Ajustes › Acerca de)
//
//  Título, texto y, si se quiere, la última captura: «Abrir en GitHub» abre el
//  issue nuevo ya escrito (Suggestion.IssueUrl) y la captura queda copiada para
//  pegarla con Ctrl+V en el issue (GitHub la sube). El issue es público: la
//  captura no se copia si no se marca.
// ----------------------------------------------------------------------------
sealed class SuggestionWindow : Window
{
    static SuggestionWindow _open;

    public static void Show(Window owner)
    {
        if (_open == null)
        {
            _open = new SuggestionWindow(owner);
            _open.Closed += (s, e) => _open = null;
            _open.Show();
        }
        _open.Activate();
    }

    readonly TextBox _title = Field(multiline: false);
    readonly TextBox _text = Field(multiline: true);
    readonly CheckBox _capture = new CheckBox { Margin = new Thickness(0, 14, 0, 0) };
    readonly Session _last;
    Button _send;

    SuggestionWindow(Window owner)
    {
        Title = $"Enviar una sugerencia — {AppInfo.Name}";
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

        _last = History.List().FirstOrDefault();

        var heading = new TextBlock { Text = "Enviar una sugerencia", FontSize = 18, FontWeight = FontWeights.SemiBold };
        var intro = Secondary("Se abre GitHub con la sugerencia ya escrita: revísala y toca «Submit new issue». "
            + "Necesitas una cuenta de GitHub. Las sugerencias son públicas.");
        intro.Margin = new Thickness(0, 6, 0, 0);

        _capture.Content = new TextBlock
        {
            Text = "Copiar la última captura para pegarla en GitHub con Ctrl+V (se verá en público)",
            TextWrapping = TextWrapping.Wrap,
        };
        _capture.SetResourceReference(ForegroundProperty, "Texto");
        _capture.IsEnabled = _last != null;
        if (_last == null) _capture.ToolTip = "Todavía no hay capturas en el historial";

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        content.Children.Add(heading);
        content.Children.Add(intro);
        content.Children.Add(Label("Título"));
        content.Children.Add(_title);
        content.Children.Add(Label("¿Qué te gustaría que cambie o que tenga Popete?"));
        content.Children.Add(_text);
        content.Children.Add(_capture);

        var cancel = Ui.Btn("Cancelar", "", Close);
        cancel.MinWidth = 100;
        _send = Ui.Btn("Abrir en GitHub", "Abre el issue en el navegador para enviarlo (Ctrl+Enter)", Send, Icons.Link, "EstiloPrimario");
        _send.MinWidth = 140;
        _send.Margin = new Thickness(8, 0, 0, 0);
        _send.IsEnabled = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_send);
        var footer = new Border
        {
            Child = buttons,
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

        TextChangedEventHandler changed = (s, e) => _send.IsEnabled = _title.Text.Trim().Length > 0 || _text.Text.Trim().Length > 0;
        _title.TextChanged += changed;
        _text.TextChanged += changed;
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && _send.IsEnabled) { Send(); e.Handled = true; }
        };
        Loaded += (s, e) => _title.Focus();
    }

    void Send()
    {
        bool withCapture = _capture.IsChecked == true && _last != null;
        if (withCapture)
        {
            try
            {
                var file = File.Exists(_last.PreviewPath) ? _last.PreviewPath : _last.OriginalPath;
                withCapture = ClipboardHelper.SetPng(File.ReadAllBytes(file), _last, null); // con la huella: no abre un editor
            }
            catch (Exception) { withCapture = false; }
            if (!withCapture)
                ConfirmDialog.Inform(this, "No se pudo copiar la captura", "La sugerencia se abre igual; puedes adjuntar la imagen a mano en GitHub.");
        }

        var url = Suggestion.IssueUrl(_title.Text, _text.Text, SystemInfo(), withCapture);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ConfirmDialog.Inform(this, "No se pudo abrir el navegador", ex.Message);
            return;
        }
        Close();
    }

    /// <summary>El pie del issue: los mismos datos que «Copiar información» en Ajustes › Acerca de.</summary>
    static string SystemInfo()
    {
        string install = AppInfo.IsPackaged ? "Microsoft Store" : "sin instalar";
        return $"{AppInfo.Name} {AppInfo.VersionText} · {install} · Windows {Environment.OSVersion.Version}";
    }

    // ------------------------------------------------------------------ piezas
    static TextBox Field(bool multiline)
    {
        var box = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(1),
        };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.MinHeight = 120;
            box.MaxHeight = 260;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        box.SetResourceReference(BackgroundProperty, "Superficie");
        box.SetResourceReference(ForegroundProperty, "Texto");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Texto");
        box.SetResourceReference(BorderBrushProperty, "Borde");
        return box;
    }

    static TextBlock Label(string text) => new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 0) };

    static TextBlock Secondary(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        return t;
    }
}
