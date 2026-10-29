// ----------------------------------------------------------------------------
//  Dónde van la franja y la barra de la captura en el lugar (con tests)
//
//  La franja es el margen dibujable alrededor de la captura, cortado en el borde
//  del monitor. La barra flotante va centrada con la captura, encima de la
//  franja de arriba (pegada a la captura: una fila de barra entra en la franja)
//  si entra en el área de trabajo; si no, en la de abajo y, como último recurso,
//  adentro, abajo de la captura. Todo en DIP de la ventana del monitor.
// ----------------------------------------------------------------------------
static class InPlaceLayout
{
    /// <summary>La captura agrandada <paramref name="band"/> por lado, sin salirse del monitor.</summary>
    public static Rect Band(Rect capture, Rect monitor, double band)
    {
        var r = capture;
        r.Inflate(band, band);
        r.Intersect(monitor);
        return r;
    }

    /// <summary>
    /// La esquina superior izquierda de la barra: <paramref name="gap"/> la separa de la captura
    /// (para no tapar su borde) y de los bordes del área de trabajo.
    /// </summary>
    public static Point Bar(Rect capture, Size bar, Rect work, double gap)
    {
        double centered = capture.Left + capture.Width / 2 - bar.Width / 2;
        double x = Math.Max(work.Left + gap, Math.Min(centered, work.Right - gap - bar.Width));
        double above = capture.Top - gap - bar.Height, below = capture.Bottom + gap;
        double y = above >= work.Top + gap ? above
            : below + bar.Height <= work.Bottom - gap ? below
            : Math.Min(capture.Bottom, work.Bottom) - gap - bar.Height; // adentro, abajo
        return new Point(x, Math.Max(work.Top + gap, y));
    }

    /// <summary>
    /// Zoom alrededor del ratón: dónde queda la esquina de la captura (<paramref name="origin"/>, en el
    /// recuadro) al pasar de <paramref name="from"/> a <paramref name="to"/> para que el punto bajo
    /// <paramref name="cursor"/> no se mueva.
    /// </summary>
    public static Point ZoomAt(Point origin, Point cursor, double from, double to)
        => cursor - (cursor - origin) * (to / from);

    /// <summary>
    /// La esquina de la captura (de tamaño <paramref name="capture"/>, ya con el zoom) corrida lo justo
    /// para que del recuadro siga a la vista al menos <paramref name="keep"/> de lo que podría verse de ella.
    /// </summary>
    public static Point KeepInView(Point origin, Size capture, Size viewport, double keep = 0.2)
    {
        static double Axis(double o, double c, double vp, double keep)
        {
            double m = keep * Math.Min(c, vp);
            return Math.Clamp(o, m - c, vp - m);
        }
        return new Point(Axis(origin.X, capture.Width, viewport.Width, keep), Axis(origin.Y, capture.Height, viewport.Height, keep));
    }

    /// <summary>
    /// La barra que el usuario arrastró: donde la soltó, pero entera dentro del área de trabajo
    /// (también cuando después cambia de ancho al desplegar los colores o el grosor).
    /// </summary>
    public static Point Keep(Point wanted, Size bar, Rect work, double gap)
    {
        double x = Math.Max(work.Left + gap, Math.Min(wanted.X, work.Right - gap - bar.Width));
        double y = Math.Max(work.Top + gap, Math.Min(wanted.Y, work.Bottom - gap - bar.Height));
        return new Point(x, y);
    }
}
