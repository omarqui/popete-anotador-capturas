// ----------------------------------------------------------------------------
//  Dónde van la franja y la barra de la captura en el lugar (con tests)
//
//  La franja es el margen dibujable alrededor de la captura, cortado en el borde
//  del monitor. La barra flotante va debajo de la franja si entra en el área de
//  trabajo, si no arriba y, como último recurso, adentro, abajo de la captura.
//  Todo en DIP de la ventana del monitor.
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

    /// <summary>La esquina superior izquierda de la barra.</summary>
    public static Point Bar(Rect band, Size bar, Rect work, double gap)
    {
        double x = Math.Max(work.Left + gap, Math.Min(band.Left, work.Right - gap - bar.Width));
        double below = band.Bottom + gap, above = band.Top - gap - bar.Height;
        double y = below + bar.Height <= work.Bottom - gap ? below
            : above >= work.Top + gap ? above
            : Math.Min(band.Bottom, work.Bottom) - gap - bar.Height; // adentro, abajo
        return new Point(x, Math.Max(work.Top + gap, y));
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
