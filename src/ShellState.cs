// ----------------------------------------------------------------------------
//  Estado de la ventana del editor (%LOCALAPPDATA%\Popete\ventana.json)
//
//  Las pestañas abiertas, en orden (vuelven la próxima vez que se abre la
//  ventana, también después de reiniciar), y el lugar de la ventana. Va aparte de
//  ajustes.json porque cambia en cada pestaña que se abre, cierra o mueve, y
//  guardar los ajustes avisa a todos los editores (AppSettings.Changed).
// ----------------------------------------------------------------------------
sealed class ShellState
{
    /// <summary>Ids de las capturas abiertas en pestañas, en orden.</summary>
    public List<string> Pestanas { get; set; } = new List<string>();

    /// <summary>Lugar y tamaño de la ventana sin maximizar, en DIP. Ancho 0 = nunca se guardó.</summary>
    public double Izquierda { get; set; }
    public double Arriba { get; set; }
    public double Ancho { get; set; }
    public double Alto { get; set; }
    public bool Maximizada { get; set; }

    static readonly string FilePath = Path.Combine(AppInfo.DataRoot, "ventana.json");

    static ShellState _current;
    public static ShellState Current => _current ??= Load();

    static ShellState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<ShellState>(File.ReadAllText(FilePath));
                if (state != null)
                {
                    state.Pestanas ??= new List<string>();
                    return state;
                }
            }
        }
        catch { /* archivo dañado: se empieza sin pestañas */ }
        return new ShellState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(this, History.JsonOptions);
            History.WriteAtomic(FilePath, fs => fs.Write(bytes, 0, bytes.Length));
        }
        catch { /* sin disco: se pierde solo qué pestañas había abiertas */ }
    }
}
