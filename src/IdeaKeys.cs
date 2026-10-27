// ----------------------------------------------------------------------------
//  Teclas con el campo de una idea abierto
//
//  El campo se abre solo después de cada trazo, con el foco puesto. Recién
//  abierto y sin tocar ("pristine"), los atajos sin letra del editor (Tab, Supr,
//  Retroceso, flechas) y sus atajos con Ctrl siguen siendo del editor y lo
//  cierran sin rastro: así no se corta el flujo de dibujar. Las letras, los
//  dígitos, Ctrl+V y AltGr (Ctrl+Alt) escriben. En cuanto se escribe algo, aunque
//  después se borre, es un cuadro de texto más: solo Enter y Esc lo cierran.
//  Lo usa EditorView.IdeaKey; lo prueba tests\Popete.Tests.
// ----------------------------------------------------------------------------
enum IdeaKeyResult
{
    /// <summary>Es del campo: escribe, edita o no hace nada.</summary>
    Type,
    /// <summary>Guarda y cierra el campo (Enter).</summary>
    Save,
    /// <summary>Cierra guardando y suelta la selección; nunca cierra la pestaña (Esc).</summary>
    Escape,
    /// <summary>Cierra el campo y la tecla sigue al editor.</summary>
    PassToEditor,
}

static class IdeaKeys
{
    /// <summary>Atajos con Ctrl del editor (EditorView.OnKeyDown), los únicos que pasan con Ctrl.</summary>
    static readonly HashSet<Key> EditorCtrlKeys = new HashSet<Key>
    {
        Key.Z, Key.Y, Key.C, Key.S, Key.Enter, Key.H, Key.A, Key.OemComma,
        Key.D0, Key.NumPad0, Key.OemPlus, Key.Add, Key.OemMinus, Key.Subtract,
    };

    static readonly HashSet<Key> PlainShortcuts = new HashSet<Key>
    {
        Key.Tab, Key.Delete, Key.Back, Key.Left, Key.Right, Key.Up, Key.Down,
    };

    static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <param name="key">La tecla real (con Alt, <c>SystemKey</c>).</param>
    /// <param name="pristine">El campo se abrió vacío y todavía no se escribió nada.</param>
    public static IdeaKeyResult Decide(Key key, ModifierKeys mods, bool pristine)
    {
        if (IsModifier(key)) return IdeaKeyResult.Type; // espera la tecla que sigue
        if (key == Key.Escape) return IdeaKeyResult.Escape;
        if (key == Key.Enter && mods == ModifierKeys.None) return IdeaKeyResult.Save;
        if (!pristine) return IdeaKeyResult.Type;

        bool ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt);
        if (ctrl && alt) return IdeaKeyResult.Type; // AltGr: @, #, [, ]...
        if (ctrl) return EditorCtrlKeys.Contains(key) ? IdeaKeyResult.PassToEditor : IdeaKeyResult.Type;
        if (!alt && PlainShortcuts.Contains(key)) return IdeaKeyResult.PassToEditor;
        return IdeaKeyResult.Type;
    }
}
