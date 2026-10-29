// ----------------------------------------------------------------------------
//  Lo que el editor de una captura (EditorView) le pide a la ventana que lo aloja
//
//  Hay dos: EditorShell (una pestaña) e InPlaceWindow (la captura en el lugar).
//  El mismo EditorView puede pasar de la segunda a la primera (MoveTo) sin
//  perder el deshacer ni la selección.
// ----------------------------------------------------------------------------
interface IEditorHost
{
    /// <summary>Dueña de los diálogos; sus Activated/Deactivated sirven al cierre automático.</summary>
    Window Window { get; }

    /// <summary>La captura está en su lugar de la pantalla: sin zoom, sin desplazamiento, sin pestañas.</summary>
    bool InPlace { get; }

    /// <summary>Salida rápida (copiar y cerrar, Esc, cierre automático).</summary>
    void Dismiss(EditorView view);

    /// <summary>Soltar una imagen sobre un editor sin anotaciones: la nueva toma su lugar.</summary>
    void Replace(EditorView view, Session session);

    /// <summary>Pasar la captura a una pestaña del editor (EditorShell: ya está en una, no hace nada).</summary>
    void OpenInTab(EditorView view);
}
