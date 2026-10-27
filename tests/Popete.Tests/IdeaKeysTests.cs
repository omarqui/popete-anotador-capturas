// Teclas con el campo de una idea abierto (src/IdeaKeys.cs).
using System.Windows.Input;
using Xunit;

public class IdeaKeysTests
{
    const ModifierKeys None = ModifierKeys.None, Ctrl = ModifierKeys.Control, Shift = ModifierKeys.Shift, Alt = ModifierKeys.Alt;

    [Fact]
    public void Ctrl_solo_no_cierra_el_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.LeftCtrl, Ctrl, pristine: true));

    [Fact]
    public void Ctrl_V_pega_en_el_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.V, Ctrl, pristine: true));

    [Fact]
    public void AltGr_escribe_en_el_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.Q, Ctrl | Alt, pristine: true));

    [Fact]
    public void Ctrl_Z_recien_abierto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.Z, Ctrl, pristine: true));

    [Fact]
    public void Ctrl_Shift_Z_recien_abierto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.Z, Ctrl | Shift, pristine: true));

    [Fact]
    public void Ctrl_C_recien_abierto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.C, Ctrl, pristine: true));

    [Fact]
    public void Ctrl_Z_ya_escrito_es_del_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.Z, Ctrl, pristine: false));

    [Fact]
    public void Retroceso_recien_abierto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.Back, None, pristine: true));

    [Fact]
    public void Retroceso_tras_vaciarlo_es_del_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.Back, None, pristine: false));

    [Fact]
    public void Flecha_tras_vaciarlo_es_del_campo()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.Left, None, pristine: false));

    [Fact]
    public void Tab_recien_abierto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.Tab, None, pristine: true));

    [Fact]
    public void Enter_guarda_y_Shift_Enter_es_otra_linea()
    {
        Assert.Equal(IdeaKeyResult.Save, IdeaKeys.Decide(Key.Enter, None, pristine: false));
        Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.Enter, Shift, pristine: false));
    }

    [Fact]
    public void Esc_cierra_el_campo()
        => Assert.Equal(IdeaKeyResult.Escape, IdeaKeys.Decide(Key.Escape, None, pristine: true));

    [Fact]
    public void Ctrl_Enter_con_texto_va_al_editor()
        => Assert.Equal(IdeaKeyResult.PassToEditor, IdeaKeys.Decide(Key.Enter, Ctrl, pristine: false));

    [Fact]
    public void Una_letra_escribe()
        => Assert.Equal(IdeaKeyResult.Type, IdeaKeys.Decide(Key.A, None, pristine: true));
}
