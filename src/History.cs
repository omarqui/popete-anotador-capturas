// ----------------------------------------------------------------------------
//  Historial persistente
// ----------------------------------------------------------------------------
sealed class SessionMeta
{
    public string Id { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Huella de la captura original.</summary>
    public string Signature { get; set; }
    /// <summary>Huellas de las versiones anotadas que se copiaron al portapapeles.</summary>
    public List<string> ExportSignatures { get; set; } = new List<string>();
}

sealed class TextNote
{
    public string Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; }
    public string Color { get; set; }
}

sealed class Session
{
    public SessionMeta Meta { get; set; }
    public string Folder => Path.Combine(History.Root, Meta.Id);
    public string OriginalPath => Path.Combine(Folder, "original.png");
    public string InkPath => Path.Combine(Folder, "trazos.isf");
    public string TextsPath => Path.Combine(Folder, "textos.json");
    public string PreviewPath => Path.Combine(Folder, "anotada.png");
    public string ThumbPath => Path.Combine(Folder, "miniatura.png");
    public string MetaPath => Path.Combine(Folder, "meta.json");
}

static class History
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnotadorCapturas", "Historial");

    public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>Huellas de todas las imágenes ya vistas (originales y exportadas).</summary>
    static readonly HashSet<string> Known = new HashSet<string>();

    public static void EnsureRoot() => Directory.CreateDirectory(Root);

    public static void LoadKnownSignatures()
    {
        foreach (var s in List())
        {
            if (!string.IsNullOrEmpty(s.Meta.Signature)) Known.Add(s.Meta.Signature);
            foreach (var e in s.Meta.ExportSignatures ?? new List<string>()) Known.Add(e);
        }
    }

    public static bool IsKnown(string signature) => Known.Contains(signature);

    public static Session FindBySignature(string sig) =>
        List().FirstOrDefault(s => s.Meta.Signature == sig || (s.Meta.ExportSignatures?.Contains(sig) ?? false));

    public static Session Create(BitmapSource img, string signature)
    {
        EnsureRoot();
        var now = DateTime.Now;
        var id = now.ToString("yyyyMMdd-HHmmss-fff");
        var s = new Session
        {
            Meta = new SessionMeta
            {
                Id = id,
                Created = now,
                Updated = now,
                Width = img.PixelWidth,
                Height = img.PixelHeight,
                Signature = signature,
            },
        };
        Directory.CreateDirectory(s.Folder);
        SavePng(img, s.OriginalPath);
        SavePng(img, s.PreviewPath);
        SaveThumb(img, s.ThumbPath);
        SaveMeta(s);
        Known.Add(signature);
        return s;
    }

    public static void RegisterExport(Session s, string signature)
    {
        Known.Add(signature);
        s.Meta.ExportSignatures ??= new List<string>();
        if (s.Meta.ExportSignatures.Contains(signature)) return;
        s.Meta.ExportSignatures.Add(signature);
        if (s.Meta.ExportSignatures.Count > 20) s.Meta.ExportSignatures.RemoveAt(0);
        SaveMeta(s);
    }

    public static List<Session> List()
    {
        if (!Directory.Exists(Root)) return new List<Session>();
        return Directory.GetDirectories(Root)
            .Select(Load)
            .Where(s => s != null)
            .OrderByDescending(s => s.Meta.Updated)
            .ToList();
    }

    static Session Load(string dir)
    {
        try
        {
            var meta = JsonSerializer.Deserialize<SessionMeta>(File.ReadAllText(Path.Combine(dir, "meta.json")));
            if (meta == null) return null;
            meta.Id = Path.GetFileName(dir);
            var s = new Session { Meta = meta };
            return File.Exists(s.OriginalPath) ? s : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveMeta(Session s) =>
        WriteAtomic(s.MetaPath, fs =>
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(s.Meta, JsonOptions);
            fs.Write(bytes, 0, bytes.Length);
        });

    public static void SavePng(BitmapSource img, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        WriteAtomic(path, fs => enc.Save(fs));
    }

    public static void SaveThumb(BitmapSource img, string path)
    {
        double scale = Math.Min(1.0, 320.0 / Math.Max(img.PixelWidth, img.PixelHeight));
        BitmapSource t = scale < 1 ? new TransformedBitmap(img, new ScaleTransform(scale, scale)) : img;
        SavePng(t, path);
    }

    /// <summary>Escribe en un .tmp y luego reemplaza, para no dejar archivos a medias.</summary>
    public static void WriteAtomic(string path, Action<Stream> write)
    {
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) write(fs);
        File.Move(tmp, path, true);
    }

    /// <summary>Carga una imagen sin bloquear el archivo.</summary>
    public static BitmapSource LoadImage(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = new MemoryStream(File.ReadAllBytes(path));
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public static void Delete(Session s) => Directory.Delete(s.Folder, true);
}
