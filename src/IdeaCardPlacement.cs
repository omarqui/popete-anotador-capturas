// ----------------------------------------------------------------------------
//  Dónde se abre la tarjeta de una idea
//
//  Cerca de su número y sin tapar lo marcado (el trazo o el texto, con su
//  número), ni lo marcado de los demás números si se puede. Prueba cuatro
//  lugares: a la derecha y a la izquierda de lo marcado
//  (a la altura del número) y arriba y abajo (alineada con él). De los que no
//  tapan nada, el más cercano al número; si todos tapan, el que tapa menos.
//  Siempre dentro de la vista. Todo en coordenadas de la vista (fuera del zoom).
//  Lo usa EditorView.PlaceIdeaCard; lo prueba tests\Popete.Tests.
// ----------------------------------------------------------------------------
static class IdeaCardPlacement
{
    public const int Right = 0, Below = 1, Above = 2, Left = 3;
    public const double Gap = 14, Margin = 8;

    /// <summary>
    /// El lado libre más cercano al número. Primero no tapar lo suyo, después no tapar lo marcado
    /// de los demás números (<paramref name="others"/>), y entre esos, el más cercano.
    /// </summary>
    public static int Choose(Rect marked, Point number, Size card, Rect view, IReadOnlyList<Rect> others = null)
    {
        int best = Right;
        (double own, double rest, double distance) bestScore = (double.MaxValue, double.MaxValue, double.MaxValue);
        foreach (int side in new[] { Right, Below, Above, Left })
        {
            var rect = new Rect(Position(side, marked, number, card, view), card);
            var score = (Overlap(rect, marked), others?.Sum(o => Overlap(rect, o)) ?? 0, Distance(number, rect));
            if (score.CompareTo(bestScore) < 0)
            {
                best = side;
                bestScore = score;
            }
        }
        return best;
    }

    static double Overlap(Rect a, Rect b)
    {
        var hit = Rect.Intersect(a, b);
        return hit.IsEmpty ? 0 : hit.Width * hit.Height;
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

    static double Distance(Point p, Rect r)
    {
        double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        double dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
