// Centro real del círculo de un número después del redondeo de píxeles de WPF (src/BadgeLayout.cs).
using System.Windows;
using Xunit;

public class BadgeLayoutTests
{
    [Fact]
    public void Redondea_lugar_y_tamano_como_WPF_a_100_por_ciento()
    {
        // Lugar 84,05 → 84; tamaño 32,5 → 32 (redondeo a par, como Math.Round); centro 84 + 16
        var c = BadgeLayout.Center(new Point(100.3, 50.6), 16.25, 1.0);
        Assert.Equal(100, c.X, 9);
        Assert.Equal(50, c.Y, 9);
    }

    [Fact]
    public void Redondea_a_pixeles_del_dispositivo_con_escala()
    {
        // A 125 %: lugar 90,1 → 113 px → 90,4; tamaño 20,2 → 25 px → 20; centro 90,4 + 10
        var c = BadgeLayout.Center(new Point(100.2, 50), 10.1, 1.25);
        Assert.Equal(100.4, c.X, 9);
        Assert.Equal(50, c.Y, 9);
    }
}
