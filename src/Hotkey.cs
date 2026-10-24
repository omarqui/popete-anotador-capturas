// ----------------------------------------------------------------------------
//  Combinación de teclas global (el atajo del historial rápido)
//
//  Se guarda en ajustes.json con los nombres de WPF ("Alt" + "OemTilde"). Las
//  banderas de ModifierKeys valen lo mismo que las de RegisterHotKey (MOD_ALT = 1,
//  MOD_CONTROL = 2, MOD_SHIFT = 4), así que CaptureHost las pasa tal cual.
// ----------------------------------------------------------------------------
sealed record Hotkey(ModifierKeys Modificadores, Key Tecla)
{
    /// <summary>Alt+` (la tecla bajo Esc): Ditto ya usa Ctrl+` y VS Code, Ctrl+Shift+`.</summary>
    public static readonly Hotkey Default = new Hotkey(ModifierKeys.Alt, Key.OemTilde);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    const uint MAPVK_VK_TO_CHAR = 2;

    /// <summary>
    /// Necesita Ctrl o Alt: con solo Shift robaría teclas al escribir. Sin Windows (al soltarla
    /// puede abrir Inicio) y sin las combinaciones del sistema (Alt+Tab, Alt+F4) ni Esc, que cancela.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid =>
        (Modificadores & (ModifierKeys.Control | ModifierKeys.Alt)) != 0
        && (Modificadores & ModifierKeys.Windows) == 0
        && Tecla != Key.None && Tecla != Key.Escape && !IsModifier(Tecla)
        && !(Modificadores.HasFlag(ModifierKeys.Alt) && Tecla is Key.Tab or Key.F4);

    public static bool IsModifier(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System;

    /// <summary>Como se muestra: "Alt+`", "Ctrl+Shift+H".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Modificadores.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modificadores.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modificadores.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modificadores.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(KeyText(Tecla));
            return string.Join("+", parts);
        }
    }

    /// <summary>Nombre de una tecla: el carácter que escribe en la distribución actual, o su nombre.</summary>
    static string KeyText(Key k)
    {
        switch (k)
        {
            case Key.Space: return "Espacio";
            case Key.Enter: return "Enter";
            case Key.Back: return "Retroceso";
            case Key.Delete: return "Supr";
            case Key.Insert: return "Insert";
            case Key.Home: return "Inicio";
            case Key.End: return "Fin";
            case Key.PageUp: return "RePág";
            case Key.PageDown: return "AvPág";
            case Key.Left: return "←";
            case Key.Right: return "→";
            case Key.Up: return "↑";
            case Key.Down: return "↓";
        }
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num " + (k - Key.NumPad0);
        // El bit alto marca una tecla muerta (acentos): se usa igual el carácter
        uint ch = MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(k), MAPVK_VK_TO_CHAR) & 0x7FFFFFFF;
        return ch > ' ' ? char.ToUpperInvariant((char)ch).ToString() : k.ToString();
    }
}
