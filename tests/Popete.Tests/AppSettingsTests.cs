// Ajustes guardados de versiones anteriores (src/AppSettings.cs).
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

public class AppSettingsTests
{
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

    static AppSettings Read(string json) => JsonSerializer.Deserialize<AppSettings>(json, Options);

    [Fact]
    public void De_fabrica_se_trabaja_en_el_lugar()
        => Assert.Equal(ModoTrabajo.EnElLugar, Read("{}").ModoTrabajo);

    [Fact]
    public void El_modo_que_guardo_la_1_3_0_se_ignora()
        => Assert.Equal(ModoTrabajo.EnElLugar, Read("{\"ModoTrabajo\":\"Pestanas\"}").ModoTrabajo);

    [Fact]
    public void Las_pestanas_elegidas_desde_la_1_3_1_se_respetan()
    {
        var s = Read("{\"ModoDeTrabajo\":\"Pestanas\"}");
        Assert.Equal(ModoTrabajo.Pestanas, s.ModoTrabajo);
        Assert.Contains("\"ModoDeTrabajo\"", JsonSerializer.Serialize(s, Options));
    }
}
