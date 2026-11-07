// ----------------------------------------------------------------------------
//  Buzón de sugerencias, la parte sin WPF (con tests)
//
//  Una sugerencia es un issue del repo público. Popete no la envía: arma el
//  enlace «issue nuevo» de GitHub con el título y el texto ya escritos, la
//  plantilla «sugerencia» (que le pone la etiqueta) y la versión y el sistema
//  al pie. Quien sugiere lo revisa en el navegador y toca «Submit new issue»
//  con su cuenta de GitHub. Así no hace falta servidor ni ningún token en el .exe.
// ----------------------------------------------------------------------------
static class Suggestion
{
    public const string RepoUrl = "https://github.com/omarqui/popete-anotador-capturas";
    public const string Template = "sugerencia.md";

    /// <summary>Lo que se le pide al texto antes de cortarlo: los navegadores y GitHub aceptan unos 8000 caracteres de URL.</summary>
    public const int MaxUrlLength = 7500;

    public const string Truncated = "\n\n(El texto era muy largo para el enlace y se cortó: sigue escribiendo aquí.)";
    public const string CaptureLine = "**Captura:** (pégala aquí con Ctrl+V)";

    /// <summary>Cuerpo del issue: el texto, el lugar de la captura (si se copió) y la versión y el sistema al pie.</summary>
    public static string Body(string text, string systemInfo, bool withCapture)
    {
        var parts = new List<string>();
        var t = (text ?? "").Trim();
        if (t.Length > 0) parts.Add(t);
        if (withCapture) parts.Add(CaptureLine);
        parts.Add("---\n" + systemInfo);
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// El enlace «issue nuevo» con la plantilla, el título y el cuerpo. Si no entra en
    /// <see cref="MaxUrlLength"/>, corta el texto (nunca el pie) y lo avisa al final de lo que queda.
    /// </summary>
    public static string IssueUrl(string title, string text, string systemInfo, bool withCapture)
    {
        title = (title ?? "").Trim();
        text = (text ?? "").Trim();
        if (title.Length == 0) title = FirstLine(text);
        if (title.Length > 120) title = title[..120];

        string Url(string body) =>
            $"{RepoUrl}/issues/new?template={Uri.EscapeDataString(Template)}"
            + $"&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

        var url = Url(Body(text, systemInfo, withCapture));
        if (url.Length <= MaxUrlLength) return url;

        // Se corta el texto hasta que entre (por mitades: la codificación cambia el largo de cada carácter)
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Url(Body(text[..mid] + Truncated, systemInfo, withCapture)).Length <= MaxUrlLength) lo = mid;
            else hi = mid - 1;
        }
        return Url(Body(text[..lo].TrimEnd() + Truncated, systemInfo, withCapture));
    }

    /// <summary>Sin título, el de la primera línea del texto (hasta 80 caracteres).</summary>
    static string FirstLine(string text)
    {
        var line = text.Replace("\r", "").Split('\n')[0].Trim();
        return line.Length > 80 ? line[..80].TrimEnd() + "…" : line;
    }
}
