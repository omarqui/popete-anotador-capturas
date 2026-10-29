// Qué hace el hook de teclado con cada tecla (src/SnipKeys.cs).
using System.Collections.Generic;
using Xunit;

public class SnipKeysTests
{
    const int LWin = 0x5B, RWin = 0x5C, LShift = 0xA0, LCtrl = 0xA2, LAlt = 0xA4, S = 0x53, Esc = 0x1B, A = 0x41;

    static SnipKeyAction Down(SnipKeys k, int vk) => k.OnKey(vk, down: true, injected: false);
    static SnipKeyAction Up(SnipKeys k, int vk) => k.OnKey(vk, down: false, injected: false);

    [Fact]
    public void Win_Shift_S_dispara_y_se_traga_la_S()
    {
        var k = new SnipKeys();
        Assert.Equal(SnipKeyAction.Pass, Down(k, LWin));
        Assert.Equal(SnipKeyAction.Pass, Down(k, LShift));
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
        Assert.Equal(SnipKeyAction.Swallow, Up(k, S));
        Assert.Equal(SnipKeyAction.Pass, Up(k, LShift));
        Assert.Equal(SnipKeyAction.Pass, Up(k, LWin));
    }

    [Fact]
    public void Con_la_Win_derecha_tambien()
    {
        var k = new SnipKeys();
        Down(k, RWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
    }

    [Fact]
    public void La_S_repetida_se_traga_sin_volver_a_disparar()
    {
        var k = new SnipKeys();
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
        Assert.Equal(SnipKeyAction.Swallow, Down(k, S));
        Assert.Equal(SnipKeyAction.Swallow, Down(k, S));
        Assert.Equal(SnipKeyAction.Swallow, Up(k, S));
    }

    [Fact]
    public void Con_Ctrl_o_Alt_no_es_la_combinacion()
    {
        var k = new SnipKeys();
        Down(k, LCtrl);
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S));
        Assert.Equal(SnipKeyAction.Pass, Up(k, S));

        var a = new SnipKeys();
        Down(a, LAlt);
        Down(a, LWin);
        Down(a, LShift);
        Assert.Equal(SnipKeyAction.Pass, Down(a, S));
    }

    [Fact]
    public void Sin_Win_o_sin_Shift_pasa()
    {
        var k = new SnipKeys();
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S));
        Up(k, S);
        Up(k, LShift);
        Down(k, LWin);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S));
    }

    [Fact]
    public void Las_teclas_inyectadas_pasan_y_no_cambian_el_estado()
    {
        var k = new SnipKeys();
        Assert.Equal(SnipKeyAction.Pass, k.OnKey(LWin, down: true, injected: true));
        Assert.Equal(SnipKeyAction.Pass, k.OnKey(LShift, down: true, injected: true));
        Assert.Equal(SnipKeyAction.Pass, Down(k, S)); // Win y Shift inyectadas no cuentan

        var j = new SnipKeys();
        Down(j, LWin);
        Down(j, LShift);
        Assert.Equal(SnipKeyAction.Pass, j.OnKey(S, down: true, injected: true));
    }

    [Fact]
    public void Soltar_Shift_antes_que_la_S_no_deja_nada_trabado()
    {
        var k = new SnipKeys();
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
        Up(k, LShift);
        Up(k, LWin);
        Assert.Equal(SnipKeyAction.Swallow, Up(k, S)); // la de abajo se tragó: la de arriba también
        Assert.Equal(SnipKeyAction.Pass, Down(k, S));  // después, una S sola es una S
        Assert.Equal(SnipKeyAction.Pass, Up(k, S));
    }

    [Fact]
    public void Win_que_se_solto_sin_que_el_hook_lo_viera_no_dispara()
    {
        // Win+L: Win baja aquí y sube en el escritorio seguro, donde el hook no está
        var held = new HashSet<int> { LShift };
        var k = new SnipKeys(vk => held.Contains(vk));
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S)); // escribir «Saludos» no congela la pantalla
    }

    [Fact]
    public void Una_S_que_subio_sin_que_el_hook_lo_viera_no_queda_tragada()
    {
        var held = new HashSet<int> { LWin, LShift };
        var k = new SnipKeys(vk => held.Contains(vk));
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
        held.Clear(); // la S, Win y Shift subieron lejos del hook
        Up(k, LShift);
        Up(k, LWin);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S)); // una S nueva no es una repetición
    }

    [Fact]
    public void Con_Popete_ocupado_Win_Shift_S_le_llega_a_Windows()
    {
        bool ready = false;
        var k = new SnipKeys(ready: () => ready);
        Down(k, LWin);
        Down(k, LShift);
        Assert.Equal(SnipKeyAction.Pass, Down(k, S));
        Assert.Equal(SnipKeyAction.Pass, Up(k, S));
        ready = true;
        Assert.Equal(SnipKeyAction.Trigger, Down(k, S));
    }

    [Fact]
    public void Esc_cancela_solo_durante_la_seleccion()
    {
        var k = new SnipKeys();
        Assert.Equal(SnipKeyAction.Pass, Down(k, Esc));
        Assert.Equal(SnipKeyAction.Pass, Up(k, Esc));
        k.Selecting = true;
        Assert.Equal(SnipKeyAction.Cancel, Down(k, Esc));
        Assert.Equal(SnipKeyAction.Swallow, Up(k, Esc));
        Assert.Equal(SnipKeyAction.Pass, Down(k, A));
    }
}
