// ----------------------------------------------------------------------------
//  Trazo del editor con un borde blanco fino alrededor, para que se destaque
//  sobre cualquier captura. Se dibuja dos veces: blanco y un poco más ancho
//  debajo, el color encima. El resaltador no lleva borde (es translúcido).
//
//  El borde es solo de dibujo: en el ISF se guarda un trazo común, y al cargar
//  (y al terminar un trazo a mano) se convierte con From().
// ----------------------------------------------------------------------------
sealed class NoteStroke : Stroke
{
    /// <summary>Ancho del borde blanco a cada lado, en píxeles de la captura.</summary>
    public const double Outline = 1.5;

    static readonly Color OutlineColor = Colors.White;

    public NoteStroke(StylusPointCollection points, DrawingAttributes attributes) : base(points, attributes) { }

    /// <summary>Copia un trazo común (puntos, atributos y propiedades, como el orden de numeración).</summary>
    public static NoteStroke From(Stroke s)
    {
        if (s is NoteStroke ns) return ns;
        var copy = new NoteStroke(s.StylusPoints.Clone(), s.DrawingAttributes.Clone());
        foreach (var id in s.GetPropertyDataIds()) copy.AddPropertyData(id, s.GetPropertyData(id));
        return copy;
    }

    protected override void DrawCore(DrawingContext context, DrawingAttributes attributes)
    {
        if (!attributes.IsHighlighter)
        {
            var outline = attributes.Clone();
            outline.Color = OutlineColor;
            outline.Width += 2 * Outline;
            outline.Height += 2 * Outline;
            base.DrawCore(context, outline);
        }
        base.DrawCore(context, attributes);
    }
}
