// ----------------------------------------------------------------------------
//  Editor — ideas por número
//
//  Cada número puede llevar una idea: un texto que no sale en la imagen y que
//  viaja con ella al copiar, para pasársela a una IA. La idea vive en el propio
//  elemento (propiedad del ISF en el trazo, NoteText.Idea en el texto), así se
//  mueve, se borra y se deshace con él. Cada guardado escribe la lista en
//  ideas.txt (Session.IdeasPath), que es lo que lee el historial rápido.
//  Qué número lleva cada cosa y el formato del texto están en Ideas.cs.
// ----------------------------------------------------------------------------
sealed partial class EditorView
{
    /// <summary>Idea del número del trazo (propiedad del ISF: string).</summary>
    static readonly Guid IdeaProperty = new Guid("e4b7c2a9-3d1f-4a6e-8b5c-9f0d2e7a1c46");

    /// <summary>
    /// Flecha libre (propiedad del ISF): no empezó sobre otro trazo y se numera como cualquiera.
    /// Se marcan las libres y no las unidas para que las flechas de capturas viejas, que no
    /// tienen el dato, sigan sin número.
    /// </summary>
    static readonly Guid FreeArrowProperty = new Guid("1a9e5d3c-7b2f-4c8a-b6e4-0d3f8a2c5e71");

    /// <summary>Flecha que sale de otro objeto: nunca lleva número; su idea va en el objeto.</summary>
    static bool IsTiedArrow(Stroke s) => IsArrow(s) && !s.ContainsPropertyData(FreeArrowProperty);

    static string IdeaOf(object target) => target switch
    {
        Stroke s when s.ContainsPropertyData(IdeaProperty) => s.GetPropertyData(IdeaProperty) as string,
        NoteText nt => nt.Idea,
        _ => null,
    };

    /// <summary>Pone la idea sin deshacer; vacía o de solo espacios, la quita.</summary>
    static void SetIdeaRaw(object target, string idea)
    {
        bool has = Ideas.HasText(idea);
        switch (target)
        {
            case Stroke s when has: s.AddPropertyData(IdeaProperty, idea); break;
            case Stroke s: if (s.ContainsPropertyData(IdeaProperty)) s.RemovePropertyData(IdeaProperty); break;
            case NoteText nt: nt.Idea = has ? idea : null; break;
        }
    }

    /// <summary>«1. idea», en el orden de los números; "" si no hay ninguna.</summary>
    string IdeasList() => Ideas.List(Numbering().Select(n => (n.number, IdeaOf(n.target))));
}
