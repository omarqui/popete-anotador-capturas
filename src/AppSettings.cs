// ----------------------------------------------------------------------------
//  Ajustes persistentes (%LOCALAPPDATA%\Popete\ajustes.json)
// ----------------------------------------------------------------------------
enum ModoTema { Sistema, Claro, Oscuro }

sealed class AppSettings
{
    /// <summary>Tema de la interfaz. "Sistema" sigue al de Windows.</summary>
    public ModoTema Tema { get; set; } = ModoTema.Sistema;

    /// <summary>Cuántas capturas conservar en el historial; las más viejas se eliminan solas. 0 = sin límite.</summary>
    public int MaxCapturas { get; set; } = 50;

    public static readonly int[] MaxCapturasOpciones = { 20, 50, 100, 200, 0 };

    /// <summary>Segundos que el trazo recién dibujado queda seleccionado. 0 = no se selecciona.</summary>
    public int SegundosSeleccion { get; set; } = 3;

    /// <summary>Si las capturas nuevas numeran sus trazos y textos (cada captura puede cambiarlo con su botón).</summary>
    public bool NumerarPorDefecto { get; set; } = true;

    /// <summary>Si los textos nuevos salen como etiqueta (fondo de color). Lo cambia el botón de la barra.</summary>
    public bool TextoConFondo { get; set; } = true;

    /// <summary>Después de cada trazo, pasar al siguiente color (rojo → azul → verde → morado → naranja).</summary>
    public bool RotarColores { get; set; } = true;

    /// <summary>Multiplicador del tamaño de los números (1 = el tamaño original).</summary>
    public double TamanoNumeros { get; set; } = 1.5;

    /// <summary>Grosor con el que abre el editor.</summary>
    public double GrosorInicial { get; set; } = 4;

    /// <summary>Herramienta con la que abre el editor: la última que se eligió (nunca Recortar).</summary>
    public Tool UltimaHerramienta { get; set; } = Tool.Pen;

    /// <summary>
    /// Botones del grupo de herramientas, en orden: nombres de <see cref="Tool"/> y "Otras" (el
    /// menú, donde va lo demás). null = la barra de fábrica. Lo arma EditorWindow.ToolOrder.cs.
    /// </summary>
    public List<string> BarraHerramientas { get; set; }

    /// <summary>Carpeta que propone "Guardar como"; null = Imágenes.</summary>
    public string CarpetaGuardar { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string CarpetaGuardarEfectiva =>
        !string.IsNullOrEmpty(CarpetaGuardar) && Directory.Exists(CarpetaGuardar)
            ? CarpetaGuardar
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    static readonly string FilePath = Path.Combine(AppInfo.DataRoot, "ajustes.json");

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

    /// <summary>Después de guardar: los editores abiertos se actualizan (por ejemplo, el tamaño de los números).</summary>
    public static event Action Changed;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, Options);
        History.WriteAtomic(FilePath, fs => fs.Write(bytes, 0, bytes.Length));
        Changed?.Invoke();
    }
}
