// Dónde van la franja y la barra de la captura en el lugar (src/InPlaceLayout.cs).
using System.Windows;
using Xunit;

public class InPlaceLayoutTests
{
    static readonly Rect Monitor = new Rect(0, 0, 1920, 1080);
    static readonly Rect Work = new Rect(0, 0, 1920, 1032); // sin la barra de tareas
    static readonly Size Bar = new Size(900, 96);
    const double Gap = 8;

    [Fact]
    public void La_franja_rodea_la_captura()
        => Assert.Equal(new Rect(352, 252, 596, 396), InPlaceLayout.Band(new Rect(400, 300, 500, 300), Monitor, 48));

    [Fact]
    public void La_franja_se_corta_en_el_borde_del_monitor()
        => Assert.Equal(new Rect(0, 0, 548, 348), InPlaceLayout.Band(new Rect(0, 0, 500, 300), Monitor, 48));

    [Fact]
    public void La_barra_va_debajo_si_entra()
    {
        var band = new Rect(352, 252, 596, 396);
        Assert.Equal(new Point(352, band.Bottom + Gap), InPlaceLayout.Bar(band, Bar, Work, Gap));
    }

    [Fact]
    public void La_barra_va_arriba_si_no_entra_debajo()
    {
        var band = new Rect(352, 700, 596, 300);
        Assert.Equal(new Point(352, 700 - Gap - Bar.Height), InPlaceLayout.Bar(band, Bar, Work, Gap));
    }

    [Fact]
    public void La_barra_va_adentro_si_no_entra_ni_arriba_ni_abajo()
    {
        var p = InPlaceLayout.Bar(Monitor, Bar, Work, Gap); // captura del monitor entero
        Assert.True(new Rect(Work.X, Work.Y, Work.Width, Work.Height).Contains(new Rect(p, Bar)));
        Assert.Equal(Work.Bottom - Gap - Bar.Height, p.Y);
    }

    [Fact]
    public void La_barra_no_se_sale_por_la_derecha()
    {
        var band = new Rect(1700, 200, 200, 100);
        var p = InPlaceLayout.Bar(band, Bar, Work, Gap);
        Assert.Equal(Work.Right - Gap - Bar.Width, p.X);
    }
}
