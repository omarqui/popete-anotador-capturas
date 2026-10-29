// ----------------------------------------------------------------------------
//  Ajustes persistentes (%LOCALAPPDATA%\Popete\ajustes.json)
// ----------------------------------------------------------------------------
enum ModoTema { Sistema, Claro, Oscuro }

/// <summary>Pestanas: Win+Shift+S es de Recortes y la captura abre en una pestaña. EnElLugar: Popete captura y se anota sobre la pantalla.</summary>
enum ModoTrabajo { Pestanas, EnElLugar }

sealed class AppSettings
{
    /// <summary>Tema de la interfaz. "Sistema" sigue al de Windows.</summary>
    public ModoTema Tema { get; set; } = ModoTema.Sistema;

    /// <summary>Cuántas capturas conservar en el historial; las más viejas se eliminan solas. 0 = sin límite.</summary>
    public int MaxCapturas { get; set; } = 50;

    public static readonly int[] MaxCapturasOpciones = { 20, 50, 100, 200, 0 };

    /// <summary>Segundos que el trazo recién dibujado queda seleccionado. 0 = no se selecciona.</summary>
    public int SegundosSeleccion { get; set; } = 3;

    /// <summary>Va antes de la lista de ideas al copiar (Ideas.WithHeader). Vacío: solo la lista.</summary>
    public string EncabezadoIdeas { get; set; } = Ideas.DefaultHeader;

    /// <summary>
    /// Numerar todos los trazos y textos, con idea o sin ella (menos las flechas que salen de un
    /// trazo). false: solo lo que tiene idea. No es el viejo "NumerarPorDefecto", que se ignora.
    /// </summary>
    public bool NumerarSiempre { get; set; }

    /// <summary>Si los textos nuevos salen como etiqueta (fondo de color). Lo cambia el botón de la barra.</summary>
    public bool TextoConFondo { get; set; } = true;

    /// <summary>Después de cada trazo, pasar al siguiente color (rojo → azul → verde → morado → naranja).</summary>
    public bool RotarColores { get; set; } = true;

    /// <summary>Multiplicador del tamaño de los números (1 = el tamaño original).</summary>
    public double TamanoNumeros { get; set; } = 1.5;

    /// <summary>Grosor con el que abre el editor.</summary>
    public double GrosorInicial { get; set; } = 4;

    /// <summary>Ctrl+C, el clic derecho y el botón "Copiar" cierran el editor después de copiar. Ctrl+Enter cierra siempre.</summary>
    public bool CerrarAlCopiar { get; set; } = true;

    /// <summary>Preguntarle a la Store por una versión nueva al rato de arrancar y cada 6 horas (solo en el MSIX).</summary>
    public bool BuscarActualizaciones { get; set; } = true;

    /// <summary>
    /// La última versión («1.2.0») cuyas novedades se mostraron, o la instalada si no hacía falta
    /// mostrarlas (WhatsNewWindow). null = Popete 1.1.3 o anterior, o una instalación nueva.
    /// </summary>
    public string UltimaVersionVista { get; set; }

    /// <summary>
    /// Modo de trabajo. Con EnElLugar, Popete se queda con Win+Shift+S (KeyboardHook), elige el recuadro
    /// él mismo (ScreenSnip) y la captura se anota donde estaba (InPlaceWindow). De fábrica, pestañas.
    /// </summary>
    public ModoTrabajo ModoTrabajo { get; set; } = ModoTrabajo.Pestanas;

    /// <summary>Atajo global del historial rápido (QuickHistoryWindow). null = sin atajo.</summary>
    public Hotkey AtajoHistorial { get; set; } = Hotkey.Default;

    /// <summary>Herramienta con la que abre el editor: la última que se eligió (nunca Recortar).</summary>
    public Tool UltimaHerramienta { get; set; } = Tool.Pen;

    /// <summary>
    /// Botones del grupo de herramientas, en orden: nombres de <see cref="Tool"/> y "Otras" (el
    /// menú, donde va lo demás). null = la barra de fábrica. Lo arma EditorView.ToolOrder.cs.
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

    /// <summary>Si al arrancar ya había ajustes.json: quien ya usaba Popete, no una instalación nueva.</summary>
    public static bool HadFile { get; private set; }

    static AppSettings Load()
    {
        try
        {
            HadFile = File.Exists(FilePath);
            if (HadFile)
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
