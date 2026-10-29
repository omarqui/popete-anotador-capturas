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

    // La barra de una fila (46 DIP) entra en la franja de 48: va encima de la franja, pegada a la captura
    static readonly Size Row = new Size(400, 46);
    const double Snug = 4;

    [Fact]
    public void La_barra_va_arriba_de_la_captura_y_centrada_con_ella()
    {
        var capture = new Rect(400, 300, 500, 300);
        Assert.Equal(new Point(650 - 200, 300 - Snug - 46), InPlaceLayout.Bar(capture, Row, Work, Snug));
    }

    [Fact]
    public void La_barra_va_debajo_si_no_entra_arriba()
    {
        var capture = new Rect(400, 30, 500, 300); // pegada al borde de arriba del monitor
        Assert.Equal(new Point(450, capture.Bottom + Snug), InPlaceLayout.Bar(capture, Row, Work, Snug));
    }

    [Fact]
    public void La_barra_va_adentro_si_no_entra_ni_arriba_ni_abajo()
    {
        var p = InPlaceLayout.Bar(Monitor, Row, Work, Snug); // captura del monitor entero
        Assert.True(Work.Contains(new Rect(p, Row)));
        Assert.Equal(new Point(960 - 200, Work.Bottom - Snug - 46), p);
    }

    [Fact]
    public void La_barra_centrada_no_se_sale_por_los_costados()
    {
        Assert.Equal(Work.Right - Snug - Row.Width, InPlaceLayout.Bar(new Rect(1800, 400, 100, 100), Row, Work, Snug).X);
        Assert.Equal(Work.Left + Snug, InPlaceLayout.Bar(new Rect(10, 400, 100, 100), Row, Work, Snug).X);
    }

    [Fact]
    public void Una_barra_mas_ancha_que_la_captura_igual_queda_centrada()
        => Assert.Equal(700 - 200, InPlaceLayout.Bar(new Rect(650, 400, 100, 60), Row, Work, Snug).X);

    [Fact]
    public void La_barra_arrastrada_queda_donde_se_solto()
        => Assert.Equal(new Point(300, 400), InPlaceLayout.Keep(new Point(300, 400), Bar, Work, Gap));

    [Fact]
    public void La_barra_arrastrada_no_se_sale_del_area_de_trabajo()
    {
        Assert.Equal(new Point(Work.Right - Gap - Bar.Width, Work.Bottom - Gap - Bar.Height),
            InPlaceLayout.Keep(new Point(1500, 1000), Bar, Work, Gap)); // por la derecha y por abajo (la barra de tareas)
        Assert.Equal(new Point(Gap, Gap), InPlaceLayout.Keep(new Point(-50, -20), Bar, Work, Gap));
    }

    [Fact]
    public void El_zoom_deja_quieto_el_punto_bajo_el_raton()
    {
        // La captura empieza en (48, 48) del recuadro; el ratón está en (148, 98), 100×50 DIP adentro
        var origin = InPlaceLayout.ZoomAt(new Point(48, 48), new Point(148, 98), 1, 2);
        Assert.Equal(new Point(148 - 200, 98 - 100), origin); // al doble, ese punto queda 200×100 adentro
        Assert.Equal(new Point(48, 48), InPlaceLayout.ZoomAt(origin, new Point(148, 98), 2, 1)); // y vuelve
    }

    [Fact]
    public void Alejar_achica_la_captura_hacia_el_raton()
        => Assert.Equal(new Point(98, 73), InPlaceLayout.ZoomAt(new Point(48, 48), new Point(148, 98), 1, 0.5));

    [Fact]
    public void Una_captura_en_su_lugar_no_se_corre()
        => Assert.Equal(new Point(48, 48), InPlaceLayout.KeepInView(new Point(48, 48), new Size(500, 300), new Size(596, 396)));

    [Fact]
    public void Mover_la_captura_agrandada_no_la_saca_del_recuadro()
    {
        var capture = new Size(1000, 600); // al doble, en un recuadro de 596×396
        var viewport = new Size(596, 396);
        // Queda a la vista al menos un 20 % del recuadro de captura
        Assert.Equal(new Point(596 - 596 * 0.2, 396 - 396 * 0.2), InPlaceLayout.KeepInView(new Point(900, 900), capture, viewport));
        Assert.Equal(new Point(596 * 0.2 - 1000, 396 * 0.2 - 600), InPlaceLayout.KeepInView(new Point(-5000, -5000), capture, viewport));
    }

    [Fact]
    public void Una_barra_mas_ancha_que_el_area_queda_pegada_a_la_izquierda()
        => Assert.Equal(Gap, InPlaceLayout.Keep(new Point(500, 300), new Size(2000, 96), Work, Gap).X);
}
