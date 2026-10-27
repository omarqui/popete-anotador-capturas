// ----------------------------------------------------------------------------
//  Ideas por número: la parte que no depende de WPF
//
//  Cada número de una captura puede llevar una idea (un texto que no sale en la
//  imagen). Aquí se decide qué número lleva cada elemento y cómo queda el texto
//  que se copia junto a la imagen. Lo prueba tests\Popete.Tests.
// ----------------------------------------------------------------------------

/// <summary>Un elemento que puede llevar número: su orden de creación y si tiene idea.</summary>
readonly record struct NumberCandidate(long Order, bool HasIdea);

static class Ideas
{
    public const string DefaultHeader = "En la imagen, cada número marca un punto:";

    const string NewLine = "\r\n";

    /// <summary>Una idea vacía o de solo espacios es como no tenerla.</summary>
    public static bool HasText(string idea) => !string.IsNullOrWhiteSpace(idea);

    /// <summary>
    /// El número de cada candidato, en el mismo índice (0 = sin número). Con
    /// <paramref name="numberAll"/>, todos 1..n por orden; si no, solo los que tienen idea, seguidos.
    /// </summary>
    public static int[] Assign(IReadOnlyList<NumberCandidate> items, bool numberAll)
    {
        var numbers = new int[items.Count];
        int n = 1;
        foreach (int i in Enumerable.Range(0, items.Count).OrderBy(i => items[i].Order)) // OrderBy es estable: empates por índice
            if (numberAll || items[i].HasIdea) numbers[i] = n++;
        return numbers;
    }

    /// <summary>El número que tomaría <paramref name="items"/>[<paramref name="index"/>] si tuviera idea (numeración apagada).</summary>
    public static int GhostNumber(IReadOnlyList<NumberCandidate> items, int index)
    {
        var withIdea = items.ToArray();
        withIdea[index] = withIdea[index] with { HasIdea = true };
        return Assign(withIdea, numberAll: false)[index];
    }

    /// <summary>
    /// «1. idea», una por línea y por número; las líneas siguientes de una idea van con
    /// sangría, alineadas con su texto. "" si no hay ninguna.
    /// </summary>
    public static string List(IEnumerable<(int number, string idea)> items)
    {
        var lines = new List<string>();
        foreach (var (number, idea) in items.Where(i => HasText(i.idea)).OrderBy(i => i.number))
        {
            var prefix = $"{number}. ";
            var parts = idea.Trim().Replace("\r\n", "\n").Split('\n');
            lines.Add(prefix + parts[0].TrimEnd());
            foreach (var part in parts.Skip(1))
                lines.Add(part.Trim().Length == 0 ? "" : new string(' ', prefix.Length) + part.TrimEnd());
        }
        return string.Join(NewLine, lines);
    }

    /// <summary>El texto que se copia: encabezado, línea en blanco y la lista. null si no hay ideas.</summary>
    public static string WithHeader(string header, string list)
    {
        if (string.IsNullOrEmpty(list)) return null;
        return string.IsNullOrWhiteSpace(header) ? list : header.Trim() + NewLine + NewLine + list;
    }
}
