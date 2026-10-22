// ----------------------------------------------------------------------------
//  Editor — soltar imágenes
//
//  Se puede arrastrar una imagen (archivo del Explorador o imagen de otra app)
//  sobre el editor. Si este editor todavía no tiene anotaciones, la imagen lo
//  reemplaza en la misma ventana; si ya tiene algo, se abre en otra ventana.
//  La captura reemplazada queda en el historial.
// ----------------------------------------------------------------------------
sealed partial class EditorWindow
{
    /// <summary>Sin trazos, textos ni recorte: se puede reusar la ventana para otra imagen.</summary>
    public bool IsEmpty => _ink.Strokes.Count == 0 && !TextBlocks.Any() && !IsCropped;

    void EnableDrop()
    {
        AllowDrop = true;
        // Preview: el editor decide antes que el cuadro de texto o la tinta
        PreviewDragOver += (s, e) =>
        {
            if (!CaptureHost.HasImages(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
            _status.Text = IsEmpty ? "Suelta para abrir la imagen aquí" : "Suelta para abrir la imagen en otra ventana";
            e.Handled = true;
        };
        PreviewDragLeave += (s, e) => _status.Text = "";
        PreviewDrop += (s, e) =>
        {
            if (!CaptureHost.HasImages(e.Data)) return;
            e.Handled = true;
            _status.Text = "";
            CommitText();
            if (CaptureHost.OpenDropped(e.Data, this) == 0)
                _status.Text = "No se pudo abrir esa imagen.";
        };
    }

    /// <summary>Abre <paramref name="session"/> en lugar de esta captura, en la misma posición y tamaño.</summary>
    public void ReplaceWith(Session session)
    {
        var next = new EditorWindow(session)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
        };
        next.Show();
        if (WindowState == WindowState.Maximized) next.WindowState = WindowState.Maximized;
        Close();
    }
}
