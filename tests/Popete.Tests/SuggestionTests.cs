// Buzón de sugerencias (src/Suggestion.cs).
using System;
using Xunit;

public class SuggestionTests
{
    const string Info = "Popete 1.3.1 · Microsoft Store · Windows 10.0.26200";

    static string Param(string url, string name)
    {
        var query = url[(url.IndexOf('?') + 1)..];
        foreach (var pair in query.Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv[0] == name) return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    [Fact]
    public void El_enlace_va_al_issue_nuevo_del_repo_con_la_plantilla()
    {
        var url = Suggestion.IssueUrl("Más colores", "Un violeta", Info, false);
        Assert.StartsWith(Suggestion.RepoUrl + "/issues/new?", url);
        Assert.Equal("sugerencia.md", Param(url, "template"));
        Assert.Equal("Más colores", Param(url, "title"));
    }

    [Fact]
    public void El_cuerpo_lleva_el_texto_y_la_version_al_pie()
    {
        var body = Param(Suggestion.IssueUrl("T", "Un violeta & un naranja", Info, false), "body");
        Assert.Equal("Un violeta & un naranja\n\n---\n" + Info, body);
    }

    [Fact]
    public void Con_captura_deja_el_lugar_para_pegarla()
    {
        var body = Param(Suggestion.IssueUrl("T", "Mira esto", Info, true), "body");
        Assert.Contains(Suggestion.CaptureLine, body);
        Assert.True(body.IndexOf(Suggestion.CaptureLine) < body.IndexOf(Info));
    }

    [Fact]
    public void Sin_titulo_usa_la_primera_linea_del_texto()
    {
        var url = Suggestion.IssueUrl("  ", "Que el resaltador sea más ancho\nporque no se ve", Info, false);
        Assert.Equal("Que el resaltador sea más ancho", Param(url, "title"));
    }

    [Fact]
    public void Un_texto_muy_largo_se_corta_sin_perder_el_pie()
    {
        var url = Suggestion.IssueUrl("Largo", new string('ñ', 20000), Info, true);
        Assert.True(url.Length <= Suggestion.MaxUrlLength);
        var body = Param(url, "body");
        Assert.Contains(Suggestion.Truncated.Trim(), body);
        Assert.Contains(Suggestion.CaptureLine, body);
        Assert.EndsWith(Info, body);
    }

    [Fact]
    public void Un_texto_que_entra_no_se_corta()
    {
        var body = Param(Suggestion.IssueUrl("T", new string('a', 3000), Info, false), "body");
        Assert.DoesNotContain(Suggestion.Truncated.Trim(), body);
    }
}
