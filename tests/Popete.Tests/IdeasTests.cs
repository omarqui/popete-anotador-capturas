// Lógica pura de las ideas por número (src/Ideas.cs): qué número lleva cada cosa y el texto que se copia.
using Xunit;

public class IdeasTests
{
    [Fact]
    public void Assign_todo_numera_por_orden()
        => Assert.Equal(new[] { 2, 1, 3 }, Ideas.Assign(new[] { new NumberCandidate(5, false), new NumberCandidate(2, true), new NumberCandidate(9, false) }, numberAll: true));

    [Fact]
    public void Assign_sin_todo_numera_seguido_solo_las_ideas()
        => Assert.Equal(new[] { 0, 1, 0, 2 }, Ideas.Assign(new[] { new NumberCandidate(1, false), new NumberCandidate(2, true), new NumberCandidate(3, false), new NumberCandidate(4, true) }, numberAll: false));

    [Fact]
    public void GhostNumber_es_el_lugar_entre_las_ideas()
        => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, true), new NumberCandidate(2, false), new NumberCandidate(3, true) }, 1));

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
        => Assert.Empty(Ideas.Assign(new NumberCandidate[0], numberAll: true));

    [Fact]
    public void GhostNumber_de_uno_que_ya_tiene_idea_es_su_numero()
        => Assert.Equal(2, Ideas.GhostNumber(new[] { new NumberCandidate(1, true), new NumberCandidate(2, true) }, 1));

    [Fact]
    public void ForAi_ruta_y_texto()
        => Assert.Equal("C:\\h\\anotada.png\r\n\r\nEn la imagen, cada número marca un punto:\r\n\r\n1. a",
            Ideas.ForAi(@"C:\h\anotada.png", Ideas.DefaultHeader, "1. a"));

    [Fact]
    public void ForAi_ruta_con_espacios_va_entre_comillas()
        => Assert.Equal("\"C:\\Mis capturas\\anotada.png\"\r\n\r\n1. a", Ideas.ForAi(@"C:\Mis capturas\anotada.png", "", "1. a"));

    [Fact]
    public void ForAi_sin_ideas_es_null()
        => Assert.Null(Ideas.ForAi(@"C:\h\anotada.png", Ideas.DefaultHeader, ""));

    [Fact]
    public void WithHeader_casos()
    {
        Assert.Null(Ideas.WithHeader(Ideas.DefaultHeader, ""));
        Assert.Equal("1. a", Ideas.WithHeader("  ", "1. a"));
        Assert.Equal("En la imagen, cada número marca un punto:\r\n\r\n1. a", Ideas.WithHeader(Ideas.DefaultHeader, "1. a"));
    }
}
