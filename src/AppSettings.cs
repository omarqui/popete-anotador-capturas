// ----------------------------------------------------------------------------
//  Ajustes persistentes (%LOCALAPPDATA%\AnotadorCapturas\ajustes.json)
// ----------------------------------------------------------------------------
enum ModoTema { Sistema, Claro, Oscuro }

sealed class AppSettings
{
    /// <summary>Tema de la interfaz. "Sistema" sigue al de Windows.</summary>
    public ModoTema Tema { get; set; } = ModoTema.Sistema;

    static readonly string FilePath = Path.Combine(Path.GetDirectoryName(History.Root), "ajustes.json");

    static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    static AppSettings _current;
    public static AppSettings Current => _current ??= Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch { /* archivo dañado: se usan los valores por defecto */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Options);
        History.WriteAtomic(FilePath, fs => fs.Write(bytes, 0, bytes.Length));
    }
}
