// Lógica pura de las ideas por número (src/Ideas.cs): qué número lleva cada cosa y el texto que se copia.
using Xunit;

public class IdeasTests
{
    [Fact]
    public void Assign_numera_seguido_solo_las_ideas()
        => Assert.Equal(new[] { 0, 1, 0, 2 }, Ideas.Assign(new[] { new NumberCandidate(1, false), new NumberCandidate(2, true), new NumberCandidate(3, false), new NumberCandidate(4, true) }));

    [Fact]
    public void Assign_sigue_el_orden_de_creacion()
        => Assert.Equal(new[] { 2, 1, 0 }, Ideas.Assign(new[] { new NumberCandidate(5, true), new NumberCandidate(2, true), new NumberCandidate(9, false) }));

    [Fact]
    public void GhostNumber_es_el_lugar_entre_las_ideas()
        => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, true), new NumberCandidate(2, false), new NumberCandidate(3, true) }, 1));

    [Fact]
    public void Assign_con_todo_numera_tambien_lo_que_no_tiene_idea()
        => Assert.Equal(new[] { 1, 2, 3, 4 }, Ideas.Assign(new[] { new NumberCandidate(1, false), new NumberCandidate(2, true), new NumberCandidate(3, false), new NumberCandidate(4, true) }, all: true));

    [Fact]
    public void Assign_con_todo_sigue_el_orden_de_creacion()
        => Assert.Equal(new[] { 2, 1, 3 }, Ideas.Assign(new[] { new NumberCandidate(5, true), new NumberCandidate(2, false), new NumberCandidate(9, false) }, all: true));

    [Fact]
    public void GhostNumber_con_todo_es_el_que_ya_lleva()
        => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, false), new NumberCandidate(2, false), new NumberCandidate(3, true) }, 1, all: true));

    [Fact]
    public void List_con_todo_conserva_los_numeros_de_la_imagen()
        => Assert.Equal("2. Dos\r\n4. Cuatro", Ideas.List(new[] { (1, (string)null), (2, "Dos"), (3, ""), (4, "Cuatro") }));

    [Fact]
    public void HasText_blanco_no_es_idea()
    {
        Assert.False(Ideas.HasText(null));
        Assert.False(Ideas.HasText("  \r\n "));
        Assert.True(Ideas.HasText(" a "));
    }

    [Fact]
    public void List_salta_vacias_y_ordena()
        => Assert.Equal("1. Uno\r\n3. Tres", Ideas.List(new[] { (3, "Tres"), (2, "   "), (1, " Uno ") }));

    [Fact]
    public void List_sangra_las_lineas_siguientes()
        => Assert.Equal("3. Este botón debería decir «Guardar».\r\n   Y estar a la derecha.",
            Ideas.List(new[] { (3, "Este botón debería decir «Guardar».\nY estar a la derecha.\r\n\r\n") }));

    [Fact]
    public void List_sangra_numeros_de_dos_cifras()
        => Assert.Equal("12. a\r\n    b", Ideas.List(new[] { (12, "a\nb") }));

    [Fact]
    public void List_quita_las_lineas_en_blanco_de_adentro()
        => Assert.Equal("1. a\r\n   b", Ideas.List(new[] { (1, "a\n\n  \nb") }));

    [Fact]
    public void Assign_sin_candidatos()
        => Assert.Empty(Ideas.Assign(new NumberCandidate[0]));

    [Fact]
    public void GhostNumber_de_uno_que_ya_tiene_idea_es_su_numero()
        => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, true), new NumberCandidate(2, true) }, 1));

    [Fact]
    public void WithHeader_casos()
    {
        Assert.Null(Ideas.WithHeader(Ideas.DefaultHeader, ""));
        Assert.Equal("1. a", Ideas.WithHeader("  ", "1. a"));
        Assert.Equal("En la imagen, cada número marca un punto:\r\n\r\n1. a", Ideas.WithHeader(Ideas.DefaultHeader, "1. a"));
    }
}
