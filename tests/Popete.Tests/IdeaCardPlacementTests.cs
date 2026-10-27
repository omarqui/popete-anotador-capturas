// Dónde se abre la tarjeta de una idea (src/IdeaCardPlacement.cs): cerca del número, sin tapar lo marcado.
using System.Windows;
using Xunit;

public class IdeaCardPlacementTests
{
    static readonly Rect View = new Rect(0, 0, 1000, 700);
    static readonly Size Card = new Size(320, 120);

    static Rect Placed(Rect marked, Point number)
        => new Rect(IdeaCardPlacement.Position(IdeaCardPlacement.Choose(marked, number, Card, View), marked, number, Card, View), Card);

    [Fact]
    public void Con_lugar_va_justo_encima_del_numero()
    {
        var marked = new Rect(100, 200, 600, 80);
        Assert.Equal(IdeaCardPlacement.Above, IdeaCardPlacement.Choose(marked, new Point(100, 200), Card, View));
        Assert.Equal(200 - IdeaCardPlacement.Gap - Card.Height, Placed(marked, new Point(100, 200)).Y);
    }

    [Fact]
    public void Sin_lugar_arriba_va_a_la_izquierda_del_numero()
    {
        var marked = new Rect(400, 10, 500, 200);
        Assert.Equal(IdeaCardPlacement.Left, IdeaCardPlacement.Choose(marked, new Point(400, 10), Card, View));
    }

    [Fact]
    public void Si_lo_marcado_ocupa_el_ancho_de_arriba_va_abajo()
    {
        var marked = new Rect(20, 10, 960, 100);
        Assert.Equal(IdeaCardPlacement.Below, IdeaCardPlacement.Choose(marked, new Point(20, 10), Card, View));
    }

    [Fact]
    public void Nunca_tapa_lo_marcado_si_hay_un_lugar_libre()
    {
        var marked = new Rect(100, 200, 600, 80);
        Assert.False(Placed(marked, new Point(100, 200)).IntersectsWith(marked));
    }

    [Fact]
    public void Tampoco_tapa_lo_marcado_de_otros_numeros()
    {
        var marked = new Rect(100, 300, 600, 80);
        var others = new[] { new Rect(60, 120, 700, 70) }; // otra anotación justo encima
        Assert.Equal(IdeaCardPlacement.Below, IdeaCardPlacement.Choose(marked, new Point(100, 300), Card, View, others));
    }

    [Fact]
    public void Siempre_queda_dentro_de_la_vista()
    {
        var marked = new Rect(0, 0, 1000, 700); // lo tapa todo: elige el que tape menos, pero dentro
        var p = Placed(marked, new Point(0, 0));
        Assert.InRange(p.X, IdeaCardPlacement.Margin, View.Width - Card.Width - IdeaCardPlacement.Margin);
        Assert.InRange(p.Y, IdeaCardPlacement.Margin, View.Height - Card.Height - IdeaCardPlacement.Margin);
    }
}
