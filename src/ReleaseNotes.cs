// ----------------------------------------------------------------------------
//  Novedades de la versión: la parte que no depende de WPF
//
//  El texto es el mismo que se pega en Partner Center («What's new in this
//  version») y empieza siempre con «Novedades de la versión X.Y.Z:». Popete lo
//  pide a la Store y, si no responde o trae las de otra versión, usa la copia
//  de novedades.txt que viaja embebida en el .exe. Lo prueba tests\Popete.Tests.
// ----------------------------------------------------------------------------
static class ReleaseNotes
{
    static readonly System.Text.RegularExpressions.Regex VersionPattern = new(@"\d+\.\d+\.\d+");

    /// <summary>La versión («1.2.0») de la primera línea con texto; null si no la dice.</summary>
    public static string VersionOf(string notes)
    {
        var first = notes?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (first == null) return null;
        var m = VersionPattern.Match(first);
        return m.Success ? m.Value : null;
    }

    /// <summary>
    /// Las novedades del JSON del catálogo de la Store (<c>StoreProduct.ExtendedJsonData</c>): el
    /// primer <c>ReleaseNotes</c> con texto, esté donde esté (en el catálogo va dentro del SKU).
    /// </summary>
    public static string FromCatalogJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Find(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string Find(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "ReleaseNotes" && p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    return p.Value.GetString();
                var found = Find(p.Value);
                if (found != null) return found;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                var found = Find(item);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>Las de la Store si son de la versión instalada; si no, las embebidas.</summary>
    public static string Pick(string store, string embedded, string installed) =>
        store != null && VersionOf(store) == installed ? store : embedded;

    /// <summary>
    /// Si toca mostrar las novedades de <paramref name="installed"/>. Sin dato guardado
    /// (<paramref name="lastSeen"/> null: Popete 1.1.3 o anterior, o una instalación nueva), solo a
    /// quien ya usaba Popete.
    /// </summary>
    public static bool ShouldShow(string installed, string lastSeen, bool existingUser)
    {
        if (!Version.TryParse(installed, out var now)) return false;
        if (!Version.TryParse(lastSeen, out var seen)) return existingUser;
        return now > seen;
    }
}
