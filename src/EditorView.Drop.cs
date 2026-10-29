// ----------------------------------------------------------------------------
//  Editor — soltar imágenes
//
//  Se puede arrastrar una imagen (archivo del Explorador o imagen de otra app)
//  sobre el editor. Si esta pestaña todavía no tiene anotaciones, la imagen la
//  reemplaza en el mismo lugar; si ya tiene algo, se abre en otra pestaña.
//  La captura reemplazada queda en el historial.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Sin trazos, textos ni recorte: se puede reusar la pestaña para otra imagen.</summary>
    public bool IsEmpty => _ink.Strokes.Count == 0 && !TextBlocks.Any() && !IsCropped;

    void EnableDrop()
    {
        AllowDrop = true;
        // Preview: el editor decide antes que el cuadro de texto o la tinta
        PreviewDragOver += (s, e) =>
        {
            if (!CaptureHost.HasImages(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
            _status.Text = IsEmpty ? "Suelta para abrir la imagen aquí" : "Suelta para abrir la imagen en otra pestaña";
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

    /// <summary>Abre <paramref name="session"/> en lugar de esta captura, en la misma posición de la barra.</summary>
    public void ReplaceWith(Session session) => _owner.Replace(this, session);
}
