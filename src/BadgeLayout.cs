// ----------------------------------------------------------------------------
//  Centro real del círculo de un número
//
//  El editor usa UseLayoutRounding: WPF redondea a píxeles del dispositivo el
//  lugar y el tamaño de cada círculo (NumberBadge, de 2r de lado, puesto en
//  centro − r), en coordenadas de la tabla y sin contar el zoom. Lo que se
//  dibuja alrededor (el anillo de la idea y su pulso) toma este centro y se
//  ubica sin redondear: así queda concéntrico a cualquier zoom. Mismo redondeo
//  que WPF (Math.Round del valor por la escala, a par). Lo prueba
//  tests\Popete.Tests.
// ----------------------------------------------------------------------------
static class BadgeLayout
{
    /// <param name="center">El centro pedido (AutoNumberCenter).</param>
    /// <param name="r">Radio del círculo (NumberRadius).</param>
    /// <param name="dpiScale">Escala del monitor (1 = 100 %).</param>
    public static Point Center(Point center, double r, double dpiScale)
    {
        double Round(double v) => Math.Round(v * dpiScale) / dpiScale;
        double size = Round(2 * r);
        return new Point(Round(center.X - r) + size / 2, Round(center.Y - r) + size / 2);
    }
}
