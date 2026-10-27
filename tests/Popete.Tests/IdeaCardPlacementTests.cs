// Dónde se abre la tarjeta de una idea (src/IdeaCardPlacement.cs).
using System.Windows;
using Xunit;

public class IdeaCardPlacementTests
{
    static readonly Rect View = new Rect(0, 0, 1000, 700);
    static readonly Size Card = new Size(320, 120);

    [Fact]
    public void Entra_a_la_derecha_de_lo_marcado()
    {
        var marked = new Rect(100, 200, 200, 80);
        int side = IdeaCardPlacement.Choose(marked, new Point(100, 200), Card, View);
        Assert.Equal(IdeaCardPlacement.Right, side);
        Assert.Equal(300 + IdeaCardPlacement.Gap, IdeaCardPlacement.Position(side, marked, new Point(100, 200), Card, View).X);
    }

    [Fact]
    public void Si_lo_marcado_llega_al_borde_derecho_va_abajo()
    {
        var marked = new Rect(100, 200, 850, 80);
        int side = IdeaCardPlacement.Choose(marked, new Point(100, 200), Card, View);
        Assert.Equal(IdeaCardPlacement.Below, side);
        Assert.Equal(280 + IdeaCardPlacement.Gap, IdeaCardPlacement.Position(side, marked, new Point(100, 200), Card, View).Y);
    }

    [Fact]
    public void Si_tampoco_entra_abajo_va_arriba()
    {
        var marked = new Rect(100, 400, 850, 250);
        Assert.Equal(IdeaCardPlacement.Above, IdeaCardPlacement.Choose(marked, new Point(100, 400), Card, View));
    }

    [Fact]
    public void Si_solo_queda_la_izquierda_va_a_la_izquierda()
    {
        var marked = new Rect(500, 20, 480, 660);
        Assert.Equal(IdeaCardPlacement.Left, IdeaCardPlacement.Choose(marked, new Point(500, 20), Card, View));
    }

    [Fact]
    public void Siempre_queda_dentro_de_la_vista()
    {
        var marked = new Rect(0, 0, 1000, 700); // lo tapa todo: elige el que tape menos, pero dentro
        var p = IdeaCardPlacement.Position(IdeaCardPlacement.Choose(marked, new Point(0, 0), Card, View), marked, new Point(0, 0), Card, View);
        Assert.InRange(p.X, IdeaCardPlacement.Margin, View.Width - Card.Width - IdeaCardPlacement.Margin);
        Assert.InRange(p.Y, IdeaCardPlacement.Margin, View.Height - Card.Height - IdeaCardPlacement.Margin);
    }
}
