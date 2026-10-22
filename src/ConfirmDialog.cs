// ----------------------------------------------------------------------------
//  Diálogo de confirmación o aviso con el estilo de la app (reemplaza MessageBox,
//  que se ve como Windows clásico y con botones en inglés).
//
//  Al estilo de los diálogos de Windows 11: título, texto y una franja abajo con
//  los botones (el principal a la izquierda). Respeta el tema claro/oscuro.
//  Enter confirma; Esc o cerrar la ventana cancelan.
// ----------------------------------------------------------------------------
sealed class ConfirmDialog : Window
{
    bool _confirmed;

    /// <summary>Pregunta y devuelve true si se eligió <paramref name="confirm"/>.</summary>
    public static bool Ask(Window owner, string title, string message, string confirm, string cancel = "Cancelar")
    {
        var d = new ConfirmDialog(owner, title, message, confirm, cancel);
        d.ShowDialog();
        return d._confirmed;
    }

    /// <summary>Aviso con un solo botón.</summary>
    public static void Inform(Window owner, string title, string message) =>
        new ConfirmDialog(owner, title, message, "Entendido", null).ShowDialog();

    ConfirmDialog(Window owner, string title, string message, string confirm, string cancel)
    {
        Title = AppInfo.Name;
        Width = 440;
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

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            LineHeight = 20,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextoSecundario");
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        content.Children.Add(heading);
        if (!string.IsNullOrEmpty(message)) content.Children.Add(body);

        var buttons = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        var ok = Ui.Btn(confirm, "", () => Finish(true), style: "EstiloPrimario");
        ok.Margin = new Thickness(0);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        buttons.Children.Add(ok);
        if (cancel != null)
        {
            var no = Ui.Btn(cancel, "", () => Finish(false));
            no.Margin = new Thickness(8, 0, 0, 0);
            no.HorizontalAlignment = HorizontalAlignment.Stretch;
            buttons.Children.Add(no);
        }
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

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Finish(false);
            else if (e.Key == Key.Enter) Finish(true);
            else return;
            e.Handled = true;
        };
    }

    void Finish(bool confirmed)
    {
        _confirmed = confirmed;
        Close();
    }
}
