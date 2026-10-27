// Novedades de la versión (src/ReleaseNotes.cs).
using Xunit;

public class ReleaseNotesTests
{
    const string Notes130 = "Novedades de la versión 1.3.0:\n\n• Algo nuevo.\n\nVersión 1.2: otra cosa.";

    [Fact]
    public void La_version_sale_de_la_primera_linea()
        => Assert.Equal("1.3.0", ReleaseNotes.VersionOf(Notes130));

    [Fact]
    public void La_version_ignora_las_lineas_en_blanco_del_principio()
        => Assert.Equal("1.3.0", ReleaseNotes.VersionOf("\r\n  \r\n" + Notes130));

    [Fact]
    public void Sin_version_en_la_primera_linea_es_null()
        => Assert.Null(ReleaseNotes.VersionOf("Mejoras varias.\nVersión 1.2.0: otra cosa."));

    [Fact]
    public void Sin_texto_no_hay_version()
    {
        Assert.Null(ReleaseNotes.VersionOf(null));
        Assert.Null(ReleaseNotes.VersionOf(""));
    }

    [Fact]
    public void Las_notas_salen_del_sku_del_catalogo()
    {
        const string json = """
            {"ProductId":"9N90ZWQLXHZ0","LocalizedProperties":[{"ProductTitle":"Popete"}],
             "DisplaySkuAvailabilities":[{"Sku":{"LocalizedProperties":[{"SkuTitle":"Popete","ReleaseNotes":"Novedades de la versión 1.3.0:\n• Algo."}]}}]}
            """;
        Assert.Equal("Novedades de la versión 1.3.0:\n• Algo.", ReleaseNotes.FromCatalogJson(json));
    }

    [Fact]
    public void Las_notas_vacias_o_ausentes_son_null()
    {
        Assert.Null(ReleaseNotes.FromCatalogJson("""{"Skus":[{"LocalizedProperties":[{"ReleaseNotes":"  "}]}]}"""));
        Assert.Null(ReleaseNotes.FromCatalogJson("""{"ProductId":"x"}"""));
    }

    [Fact]
    public void Un_json_roto_no_lanza()
    {
        Assert.Null(ReleaseNotes.FromCatalogJson("{no es json"));
        Assert.Null(ReleaseNotes.FromCatalogJson(null));
    }

    [Fact]
    public void Se_usan_las_de_la_Store_si_son_de_la_version_instalada()
        => Assert.Equal(Notes130, ReleaseNotes.Pick(Notes130, "embebidas", "1.3.0"));

    [Fact]
    public void Se_usan_las_embebidas_si_las_de_la_Store_son_de_otra_version()
        => Assert.Equal("embebidas", ReleaseNotes.Pick(Notes130, "embebidas", "1.3.1"));

    [Fact]
    public void Se_usan_las_embebidas_sin_respuesta_de_la_Store()
        => Assert.Equal("embebidas", ReleaseNotes.Pick(null, "embebidas", "1.3.0"));

    [Fact]
    public void Una_version_nueva_se_muestra()
        => Assert.True(ReleaseNotes.ShouldShow("1.3.0", lastSeen: "1.2.0", existingUser: true));

    [Fact]
    public void La_misma_version_no_se_vuelve_a_mostrar()
        => Assert.False(ReleaseNotes.ShouldShow("1.3.0", lastSeen: "1.3.0", existingUser: true));

    [Fact]
    public void Una_version_anterior_no_se_muestra()
        => Assert.False(ReleaseNotes.ShouldShow("1.2.0", lastSeen: "1.3.0", existingUser: true));

    [Fact]
    public void Se_compara_como_version_y_no_como_texto()
        => Assert.True(ReleaseNotes.ShouldShow("1.10.0", lastSeen: "1.9.0", existingUser: true));

    [Fact]
    public void Quien_actualiza_desde_una_version_sin_el_dato_las_ve()
        => Assert.True(ReleaseNotes.ShouldShow("1.3.0", lastSeen: null, existingUser: true));

    [Fact]
    public void Una_instalacion_nueva_no_las_muestra()
        => Assert.False(ReleaseNotes.ShouldShow("1.3.0", lastSeen: null, existingUser: false));

    [Fact]
    public void Un_dato_ilegible_cuenta_como_ausente()
        => Assert.True(ReleaseNotes.ShouldShow("1.3.0", lastSeen: "basura", existingUser: true));
}
