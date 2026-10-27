// ----------------------------------------------------------------------------
//  Dónde se abre la tarjeta de una idea
//
//  Prueba cuatro lugares alrededor de lo marcado (el trazo o el texto, con su
//  número): a la derecha, abajo, arriba y a la izquierda. Se queda con el primero
//  que no lo tapa; si ninguno entra, con el que tapa menos. Siempre dentro de la
//  vista. Todo en coordenadas de la vista (fuera del zoom). Lo usa
//  EditorView.PlaceIdeaCard; lo prueba tests\Popete.Tests.
// ----------------------------------------------------------------------------
static class IdeaCardPlacement
{
    public const int Right = 0, Below = 1, Above = 2, Left = 3;
    public const double Gap = 14, Margin = 8;

    /// <summary>El lado donde no tapa lo marcado (o donde tapa menos).</summary>
    public static int Choose(Rect marked, Point number, Size card, Rect view)
    {
        int best = Right;
        double bestOverlap = double.MaxValue;
        foreach (int side in new[] { Right, Below, Above, Left })
        {
            var rect = new Rect(Position(side, marked, number, card, view), card);
            rect.Intersect(marked);
            double overlap = rect.IsEmpty ? 0 : rect.Width * rect.Height;
            if (overlap == 0) return side;
            if (overlap < bestOverlap)
            {
                best = side;
                bestOverlap = overlap;
            }
        }
        return best;
    }

    /// <summary>Esquina superior izquierda de la tarjeta en ese lado, dentro de la vista.</summary>
    public static Point Position(int side, Rect marked, Point number, Size card, Rect view)
    {
        // A los costados, a la altura del número; arriba y abajo, alineada con él
        var p = side switch
        {
            Right => new Point(marked.Right + Gap, number.Y - 18),
            Below => new Point(number.X - 12, marked.Bottom + Gap),
            Above => new Point(number.X - 12, marked.Top - Gap - card.Height),
            _ => new Point(marked.Left - Gap - card.Width, number.Y - 18),
        };
        return new Point(
            Math.Clamp(p.X, view.Left + Margin, Math.Max(view.Left + Margin, view.Right - card.Width - Margin)),
            Math.Clamp(p.Y, view.Top + Margin, Math.Max(view.Top + Margin, view.Bottom - card.Height - Margin)));
    }
}
