#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property UseWindowsForms=true
#:property PublishAot=false
#:property ImplicitUsings=disable
#:property Nullable=disable
#:property EnableDefaultPageItems=false
#:property EnableDefaultApplicationDefinition=false
#:property EnableDefaultEmbeddedResourceItems=false
#:property EnableDefaultNoneItems=false

// ============================================================================
//  Anotador de capturas — app de un solo archivo (.NET 10, file-based app)
//
//  Ejecutar:   dotnet run AnotadorCapturas.cs
//  Publicar:   dotnet publish AnotadorCapturas.cs   (genera un .exe)
//
//  Queda en la bandeja del sistema. Cada imagen NUEVA que llega al
//  portapapeles (Impr Pant, Win+Shift+S, Recortes...) abre un editor.
//  Cada captura se guarda en un historial con sus anotaciones y puede
//  retomarse desde el ícono de la bandeja.
//
//  Historial: %LOCALAPPDATA%\AnotadorCapturas\Historial\<fecha-hora>\
//     original.png   captura sin editar
//     trazos.isf     trazos (lápiz, resaltador, formas)
//     textos.json    textos
//     anotada.png    imagen con las anotaciones
//     miniatura.png  vista previa pequeña
//     meta.json      fechas, tamaño y huellas para detectar duplicados
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, @"Local\AnotadorCapturas_InstanciaUnica", out bool esPrimera);
        if (!esPrimera) return; // ya hay una instancia corriendo

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new CaptureHost(app);
        host.Start();
        app.Run();
    }
}

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

// ----------------------------------------------------------------------------
//  Escucha del portapapeles + ícono en la bandeja
// ----------------------------------------------------------------------------
sealed class CaptureHost : IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;
    static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    readonly Application _app;
    readonly DispatcherTimer _debounce;
    readonly List<System.Drawing.Image> _menuThumbs = new List<System.Drawing.Image>();
    HwndSource _source;
    System.Windows.Forms.NotifyIcon _tray;
    System.Windows.Forms.ToolStripMenuItem _resumeItem;
    System.Windows.Forms.ToolStripMenuItem _pauseItem;
    System.Windows.Forms.ToolStripMenuItem _startupItem;
    HistoryWindow _historyWindow;
    bool _paused;

    public CaptureHost(Application app)
    {
        _app = app;
        // El portapapeles suele cambiar varias veces seguidas por captura: se agrupan.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (s, e) => { _debounce.Stop(); TryOpenFromClipboard(); };
    }

    public void Start()
    {
        History.LoadKnownSignatures();

        // Ventana "message-only" invisible que recibe WM_CLIPBOARDUPDATE
        var p = new HwndSourceParameters("AnotadorCapturasListener")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
        CreateTray();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && !_paused)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }

    void TryOpenFromClipboard()
    {
        var img = ClipboardHelper.TryGetImage();
        if (img == null) return;

        // Solo se abren imágenes nunca vistas. Algunos programas (Escritorio
        // remoto, historial del portapapeles, gestores de portapapeles...)
        // vuelven a publicar la misma imagen; esas se ignoran siempre.
        var sig = ClipboardHelper.Signature(img);
        if (History.IsKnown(sig)) return;

        OpenEditor(History.Create(img, sig));
    }

    public static void OpenEditor(Session s)
    {
        if (EditorWindow.Open.TryGetValue(s.Meta.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        try
        {
            new EditorWindow(s).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Anotador de capturas",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void OpenFromClipboardManually()
    {
        var img = ClipboardHelper.TryGetImage();
        if (img == null)
        {
            _tray.ShowBalloonTip(2000, "Anotador de capturas", "No hay ninguna imagen en el portapapeles.",
                System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        var sig = ClipboardHelper.Signature(img);
        OpenEditor(History.FindBySignature(sig) ?? History.Create(img, sig));
    }

    void ShowHistory()
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow(OpenEditor);
            _historyWindow.Closed += (s, e) => _historyWindow = null;
            _historyWindow.Show();
        }
        if (_historyWindow.WindowState == WindowState.Minimized) _historyWindow.WindowState = WindowState.Normal;
        _historyWindow.Activate();
    }

    void CreateTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        _resumeItem = new System.Windows.Forms.ToolStripMenuItem("Retomar captura");
        _resumeItem.DropDownItems.Add("(cargando…)"); // para que muestre la flecha del submenú
        _resumeItem.DropDownOpening += (s, e) => BuildResumeMenu();
        menu.Items.Add(_resumeItem);
        menu.Items.Add("Ver historial…", null, (s, e) => ShowHistory());
        menu.Items.Add("Abrir imagen del portapapeles", null, (s, e) => OpenFromClipboardManually());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("Pausar detección", null, (s, e) => TogglePause());
        _startupItem = new System.Windows.Forms.ToolStripMenuItem("Iniciar con Windows", null, (s, e) => ToggleStartup())
        {
            Checked = StartupHelper.IsEnabled(),
        };
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (s, e) => _app.Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Anotador de capturas",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (s, e) =>
        {
            var last = History.List().FirstOrDefault();
            if (last != null) OpenEditor(last);
            else ShowHistory();
        };
        _tray.ShowBalloonTip(2500, "Anotador de capturas",
            "Activo. Toma una captura con Impr Pant o Win+Shift+S.", System.Windows.Forms.ToolTipIcon.Info);
    }

    void BuildResumeMenu()
    {
        foreach (var img in _menuThumbs) img.Dispose();
        _menuThumbs.Clear();
        _resumeItem.DropDownItems.Clear();

        var sessions = History.List().Take(12).ToList();
        if (sessions.Count == 0)
        {
            _resumeItem.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("(sin capturas todavía)") { Enabled = false });
            return;
        }

        foreach (var s in sessions)
        {
            var session = s;
            bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
            var item = new System.Windows.Forms.ToolStripMenuItem(
                $"{s.Meta.Updated:dd/MM HH:mm}   ·   {s.Meta.Width}×{s.Meta.Height}{(abierta ? "   (abierta)" : "")}",
                LoadMenuThumb(s.ThumbPath),
                (o, e) => OpenEditor(session))
            {
                ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
            };
            _resumeItem.DropDownItems.Add(item);
        }
        _resumeItem.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        _resumeItem.DropDownItems.Add("Ver historial completo…", null, (o, e) => ShowHistory());
    }

    System.Drawing.Image LoadMenuThumb(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var full = System.Drawing.Image.FromStream(ms);
            int h = 40;
            int w = Math.Max(1, full.Width * h / full.Height);
            if (w > 80) { w = 80; h = Math.Max(1, full.Height * w / full.Width); }
            var thumb = new System.Drawing.Bitmap(full, new System.Drawing.Size(w, h));
            _menuThumbs.Add(thumb);
            return thumb;
        }
        catch
        {
            return null;
        }
    }

    void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Reanudar detección" : "Pausar detección";
        _tray.Text = _paused ? "Anotador de capturas (en pausa)" : "Anotador de capturas";
    }

    void ToggleStartup()
    {
        bool on = !StartupHelper.IsEnabled();
        StartupHelper.Set(on);
        _startupItem.Checked = on;
    }

    public void Dispose()
    {
        if (_source != null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.Dispose();
            _source = null;
        }
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        foreach (var img in _menuThumbs) img.Dispose();
        _menuThumbs.Clear();
    }
}

// ----------------------------------------------------------------------------
//  Lectura robusta de imágenes del portapapeles
// ----------------------------------------------------------------------------
static class ClipboardHelper
{
    public static BitmapSource TryGetImage()
    {
        // El portapapeles puede estar bloqueado por otra app: se reintenta.
        for (int intento = 0; intento < 5; intento++)
        {
            try
            {
                var data = System.Windows.Forms.Clipboard.GetDataObject();
                if (data == null) return null;

                // 1) Formato PNG (Herramienta Recortes lo publica; conserva transparencia)
                if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
                    return Decode(png);

                // 2) Bitmap clásico (Impr Pant). Se pasa por PNG para evitar el
                //    problema conocido de WPF con el canal alfa de CF_DIB.
                if (data.GetDataPresent(System.Windows.Forms.DataFormats.Bitmap))
                {
                    using var image = System.Windows.Forms.Clipboard.GetImage();
                    if (image == null) return null;
                    var ms = new MemoryStream();
                    image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    return Decode(ms);
                }
                return null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        return null;
    }

    public static BitmapSource Decode(Stream s)
    {
        s.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = s;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Huella de la imagen completa (tamaño + SHA-256 de los píxeles).</summary>
    public static string Signature(BitmapSource img)
    {
        var src = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
        var buffer = new byte[stride * h];
        src.CopyPixels(buffer, stride, 0);
        return $"{w}x{h}:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer));
    }
}

// ----------------------------------------------------------------------------
//  Inicio con Windows (HKCU\...\Run)
// ----------------------------------------------------------------------------
static class StartupHelper
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AnotadorCapturas";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) != null;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(ValueName, false);
    }
}

// ----------------------------------------------------------------------------
//  Utilidades de UI
// ----------------------------------------------------------------------------
static class Ui
{
    public static Button Btn(string text, string tip, Action action)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
        b.Click += (s, e) => action();
        return b;
    }
}

// ----------------------------------------------------------------------------
//  Ventana de historial
// ----------------------------------------------------------------------------
sealed class HistoryWindow : Window
{
    readonly Action<Session> _open;
    readonly WrapPanel _panel = new WrapPanel { Margin = new Thickness(10) };
    readonly TextBlock _count = new TextBlock
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
    };

    public HistoryWindow(Action<Session> open)
    {
        _open = open;
        Title = "Historial de capturas";
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Btn("🔄 Actualizar", "Recargar la lista", Refresh));
        buttons.Children.Add(Ui.Btn("📂 Abrir carpeta", "Abrir la carpeta del historial en el Explorador", () =>
        {
            History.EnsureRoot();
            Process.Start("explorer.exe", $"\"{History.Root}\"");
        }));

        var top = new DockPanel { Margin = new Thickness(14, 8, 10, 8) };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(_count);

        var root = new DockPanel();
        var topBorder = new Border
        {
            Child = top,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        DockPanel.SetDock(topBorder, Dock.Top);
        root.Children.Add(topBorder);
        root.Children.Add(new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        // Se recarga cada vez que la ventana vuelve al frente
        Activated += (s, e) => Refresh();
    }

    void Refresh()
    {
        _panel.Children.Clear();
        var sessions = History.List();
        _count.Text = sessions.Count == 1 ? "1 captura" : $"{sessions.Count} capturas";

        if (sessions.Count == 0)
        {
            _panel.Children.Add(new TextBlock
            {
                Text = "Todavía no hay capturas. Toma una con Impr Pant o Win+Shift+S.",
                Margin = new Thickness(10),
                Foreground = Brushes.DimGray,
            });
            return;
        }
        foreach (var s in sessions) _panel.Children.Add(Card(s));
    }

    Border Card(Session s)
    {
        var thumb = new Image { Height = 150, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) };
        try { if (File.Exists(s.ThumbPath)) thumb.Source = History.LoadImage(s.ThumbPath); } catch { }

        bool abierta = EditorWindow.Open.ContainsKey(s.Meta.Id);
        var info = new TextBlock
        {
            Text = $"{s.Meta.Updated:dd/MM/yyyy HH:mm}   ·   {s.Meta.Width}×{s.Meta.Height}{(abierta ? "   ·   abierta" : "")}",
            Foreground = Brushes.DimGray,
            FontSize = 12,
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(Ui.Btn("✏ Retomar", "Abrir para seguir editando", () => _open(s)));
        bar.Children.Add(Ui.Btn("🗑 Eliminar", "Eliminar del historial", () => Delete(s)));

        var stack = new StackPanel();
        stack.Children.Add(thumb);
        stack.Children.Add(info);
        stack.Children.Add(bar);

        var card = new Border
        {
            Width = 270,
            Margin = new Thickness(6),
            Padding = new Thickness(10),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = "Doble clic para retomar",
        };
        card.MouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) _open(s); };
        return card;
    }

    void Delete(Session s)
    {
        if (EditorWindow.Open.TryGetValue(s.Meta.Id, out var w))
        {
            MessageBox.Show(this, "Esta captura está abierta. Ciérrala antes de eliminarla.", "Historial de capturas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            w.Activate();
            return;
        }
        if (MessageBox.Show(this, "¿Eliminar esta captura y sus anotaciones del historial?", "Historial de capturas",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try { History.Delete(s); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo eliminar", MessageBoxButton.OK, MessageBoxImage.Error); }
        Refresh();
    }
}

// ----------------------------------------------------------------------------
//  Deshacer
// ----------------------------------------------------------------------------
interface IUndo { void Undo(); }

sealed class StrokeChange : IUndo
{
    readonly StrokeCollection _target, _added, _removed;
    public StrokeChange(StrokeCollection target, StrokeCollection added, StrokeCollection removed)
    {
        _target = target;
        _added = new StrokeCollection(added);
        _removed = new StrokeCollection(removed);
    }
    public void Undo()
    {
        foreach (var s in _added) if (_target.Contains(s)) _target.Remove(s);
        foreach (var s in _removed) if (!_target.Contains(s)) _target.Add(s);
    }
}

sealed class ElementAdd : IUndo
{
    readonly Panel _panel; readonly UIElement _element;
    public ElementAdd(Panel panel, UIElement element) { _panel = panel; _element = element; }
    public void Undo() => _panel.Children.Remove(_element);
}

sealed class ClearAllChange : IUndo
{
    readonly StrokeCollection _target, _strokes; readonly Panel _panel; readonly List<UIElement> _elements;
    public ClearAllChange(StrokeCollection target, StrokeCollection strokes, Panel panel, List<UIElement> elements)
    { _target = target; _strokes = strokes; _panel = panel; _elements = elements; }
    public void Undo()
    {
        _target.Add(_strokes);
        foreach (var e in _elements) _panel.Children.Add(e);
    }
}

// ----------------------------------------------------------------------------
//  Editor
// ----------------------------------------------------------------------------
enum Tool { Pen, Highlighter, Line, Arrow, Rectangle, Text, Eraser }

sealed class EditorWindow : Window
{
    /// <summary>Editores abiertos por Id de sesión (para no abrir la misma captura dos veces).</summary>
    public static readonly Dictionary<string, EditorWindow> Open = new Dictionary<string, EditorWindow>();

    static readonly Color[] Palette =
    {
        Color.FromRgb(232, 17, 35),   // rojo
        Color.FromRgb(255, 214, 0),   // amarillo
        Color.FromRgb(0, 168, 80),    // verde
        Color.FromRgb(0, 120, 215),   // azul
        Color.FromRgb(255, 140, 0),   // naranja
        Colors.Black,
        Colors.White,
    };

    readonly Session _session;
    readonly BitmapSource _image;
    readonly Grid _surface;          // lo que se exporta: imagen + tinta + textos
    readonly InkCanvas _ink;
    readonly Canvas _textLayer;
    readonly ScrollViewer _scroll;
    readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
    readonly Border _flash;
    readonly DispatcherTimer _saveTimer;
    readonly Stack<IUndo> _undo = new Stack<IUndo>();
    readonly Dictionary<Tool, ToggleButton> _toolButtons = new Dictionary<Tool, ToggleButton>();
    readonly List<(Border swatch, Color color)> _swatches = new List<(Border, Color)>();
    readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _saveText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = Brushes.Gray };
    readonly TextBlock _zoomText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    Tool _tool = Tool.Pen;
    Color _penColor = Palette[0];
    Color _highlightColor = Palette[1];
    double _size = 4;
    bool _suppress;      // no registrar cambios internos en el historial de deshacer
    bool _pendingSave;
    bool _closeScheduled;
    Stroke _preview;
    Point _start;
    TextBox _editing;

    Color CurrentColor
    {
        get => _tool == Tool.Highlighter ? _highlightColor : _penColor;
        set { if (_tool == Tool.Highlighter) _highlightColor = value; else _penColor = value; }
    }

    public EditorWindow(Session session)
    {
        _session = session;
        _image = History.LoadImage(session.OriginalPath);
        int pw = _image.PixelWidth, ph = _image.PixelHeight;

        Title = $"Anotar captura — {session.Meta.Created:dd/MM/yyyy HH:mm} — {pw}×{ph}";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(work.Width * 0.85, Math.Max(760, pw + 80));
        Height = Math.Min(work.Height * 0.9, Math.Max(480, ph + 170));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));
        UseLayoutRounding = true;

        // Guardado automático en el historial, poco después de cada cambio
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (s, e) => SaveSession();

        // ---- superficie de dibujo (1 DIP = 1 píxel de la captura) ----
        var imgCtl = new Image { Source = _image, Width = pw, Height = ph, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(imgCtl, BitmapScalingMode.HighQuality);

        _ink = new InkCanvas { Background = Brushes.Transparent, Width = pw, Height = ph };
        _ink.Strokes.StrokesChanged += (s, e) =>
        {
            if (_suppress) return;
            PushUndo(new StrokeChange(_ink.Strokes, e.Added, e.Removed));
        };
        _ink.PreviewMouseLeftButtonDown += Ink_MouseDown;
        _ink.PreviewMouseMove += Ink_MouseMove;
        _ink.PreviewMouseLeftButtonUp += Ink_MouseUp;

        _textLayer = new Canvas { Width = pw, Height = ph, IsHitTestVisible = false };

        _surface = new Grid { Width = pw, Height = ph, ClipToBounds = true };
        _surface.Children.Add(imgCtl);
        _surface.Children.Add(_ink);
        _surface.Children.Add(_textLayer);

        // Capa de parpadeo: encima de la superficie pero fuera de ella,
        // para que no aparezca en la imagen exportada.
        _flash = new Border
        {
            Background = Brushes.White,
            Opacity = 0,
            IsHitTestVisible = false,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(6),
        };
        var stack = new Grid();
        stack.Children.Add(_surface);
        stack.Children.Add(_flash);

        var host = new Border
        {
            Child = stack,
            LayoutTransform = _zoom,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5 },
        };

        _scroll = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
        };
        _scroll.PreviewMouseWheel += (s, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetZoom(_zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
            e.Handled = true;
        };

        // ---- layout ----
        var root = new DockPanel();
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var statusBar = new DockPanel { Margin = new Thickness(10, 4, 10, 4) };
        DockPanel.SetDock(_zoomText, Dock.Right);
        DockPanel.SetDock(_saveText, Dock.Right);
        statusBar.Children.Add(_zoomText);
        statusBar.Children.Add(_saveText);
        statusBar.Children.Add(_status);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        root.Children.Add(_scroll);
        Content = root;

        LoadAnnotations();
        SetHint();
        SetTool(Tool.Pen);

        // PreviewKeyDown: la ventana ve las teclas antes que el InkCanvas,
        // que de otro modo se queda con Ctrl+C / Ctrl+X / Ctrl+V.
        PreviewKeyDown += OnKeyDown;

        Open[session.Meta.Id] = this;
        Closed += (s, e) => Open.Remove(session.Meta.Id);

        Loaded += (s, e) =>
        {
            FitZoom();
            // Traer al frente aunque la app esté en segundo plano
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        };
        Closing += (s, e) =>
        {
            CommitText();
            if (_pendingSave) SaveSession();
        };
    }

    // ------------------------------------------------------------------ persistencia
    void LoadAnnotations()
    {
        _suppress = true;
        try
        {
            if (File.Exists(_session.InkPath))
            {
                try
                {
                    using var fs = File.OpenRead(_session.InkPath);
                    _ink.Strokes.Add(new StrokeCollection(fs));
                }
                catch { /* archivo dañado: se ignora */ }
            }

            if (File.Exists(_session.TextsPath))
            {
                try
                {
                    var notes = JsonSerializer.Deserialize<List<TextNote>>(File.ReadAllText(_session.TextsPath));
                    foreach (var n in notes ?? new List<TextNote>()) AddTextBlock(n);
                }
                catch { /* archivo dañado: se ignora */ }
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    void MarkChanged()
    {
        _pendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SaveSession()
    {
        _saveTimer.Stop();
        if (_editing != null)
        {
            // Se está escribiendo un texto: se guarda cuando termine.
            _saveTimer.Start();
            return;
        }
        try
        {
            if (_ink.Strokes.Count > 0)
                History.WriteAtomic(_session.InkPath, fs => _ink.Strokes.Save(fs));
            else if (File.Exists(_session.InkPath))
                File.Delete(_session.InkPath);

            var notes = _textLayer.Children.OfType<TextBlock>().Select(tb => new TextNote
            {
                Text = tb.Text,
                X = Canvas.GetLeft(tb),
                Y = Canvas.GetTop(tb),
                FontSize = tb.FontSize,
                Color = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? Palette[0].ToString(),
            }).ToList();
            var json = JsonSerializer.SerializeToUtf8Bytes(notes, History.JsonOptions);
            History.WriteAtomic(_session.TextsPath, fs => fs.Write(json, 0, json.Length));

            var rendered = Render();
            History.SavePng(rendered, _session.PreviewPath);
            History.SaveThumb(rendered, _session.ThumbPath);

            _session.Meta.Updated = DateTime.Now;
            History.SaveMeta(_session);

            _pendingSave = false;
            _saveText.Text = $"Guardado en historial {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _saveText.Text = "Error al guardar: " + ex.Message;
        }
    }

    // ------------------------------------------------------------------ toolbar
    FrameworkElement BuildToolbar()
    {
        var panel = new WrapPanel { Margin = new Thickness(6, 6, 6, 4) };

        panel.Children.Add(ToolButton("✏ Lápiz", Tool.Pen, "Lápiz (P)"));
        panel.Children.Add(ToolButton("🖍 Resaltador", Tool.Highlighter, "Resaltador / subrayador (H)"));
        panel.Children.Add(ToolButton("╱ Línea", Tool.Line, "Línea recta (L) — Shift: ángulos de 45°"));
        panel.Children.Add(ToolButton("➜ Flecha", Tool.Arrow, "Flecha (A) — Shift: ángulos de 45°"));
        panel.Children.Add(ToolButton("▭ Rectángulo", Tool.Rectangle, "Rectángulo (R) — Shift: cuadrado"));
        panel.Children.Add(ToolButton("T Texto", Tool.Text, "Texto (T) — clic para escribir"));
        panel.Children.Add(ToolButton("⌫ Borrador", Tool.Eraser, "Borrar trazos (E)"));

        panel.Children.Add(Divider());

        foreach (var c in Palette)
        {
            var sw = new Border
            {
                Width = 24, Height = 24,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(c),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var color = c;
            sw.MouseLeftButtonUp += (s, e) =>
            {
                CurrentColor = color;
                RefreshSwatches();
                UpdateDrawingAttributes();
                if (_editing != null) _editing.Foreground = new SolidColorBrush(color);
            };
            _swatches.Add((sw, c));
            panel.Children.Add(sw);
        }

        panel.Children.Add(Divider());

        panel.Children.Add(new TextBlock { Text = "Grosor", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        var slider = new Slider { Minimum = 1, Maximum = 20, Value = _size, Width = 110, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
        slider.ValueChanged += (s, e) =>
        {
            _size = e.NewValue;
            UpdateDrawingAttributes();
            if (_editing != null) _editing.FontSize = FontSizeFor(_size);
        };
        panel.Children.Add(slider);

        panel.Children.Add(Divider());

        panel.Children.Add(Ui.Btn("↶ Deshacer", "Deshacer (Ctrl+Z)", Undo));
        panel.Children.Add(Ui.Btn("🗑 Limpiar", "Quitar todas las anotaciones", ClearAll));
        panel.Children.Add(Ui.Btn("Ajustar", "Ajustar a la ventana (Ctrl+0)", FitZoom));
        panel.Children.Add(Ui.Btn("1:1", "Tamaño real", () => SetZoom(1 / DpiScale())));

        panel.Children.Add(Divider());

        panel.Children.Add(Ui.Btn("📋 Copiar", "Copiar imagen anotada sin cerrar", () => CopyToClipboard()));
        panel.Children.Add(Ui.Btn("💾 Guardar", "Guardar como PNG/JPG (Ctrl+S)", Save));
        panel.Children.Add(Ui.Btn("✔ Copiar y cerrar", "Copiar y cerrar (Ctrl+C o Ctrl+Enter)", () => { if (CopyToClipboard()) Close(); }));

        return new Border
        {
            Child = panel,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    ToggleButton ToolButton(string text, Tool t, string tip)
    {
        var b = new ToggleButton { Content = text, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
        b.Click += (s, e) => SetTool(t);
        _toolButtons[t] = b;
        return b;
    }

    static Border Divider() => new Border
    {
        Width = 1,
        Margin = new Thickness(6, 4, 6, 4),
        Background = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
    };

    void RefreshSwatches()
    {
        var current = CurrentColor;
        foreach (var (sw, c) in _swatches)
        {
            bool sel = c == current;
            sw.BorderThickness = new Thickness(sel ? 3 : 1);
            sw.BorderBrush = sel ? Brushes.DodgerBlue : Brushes.DimGray;
        }
    }

    // ------------------------------------------------------------------ herramientas
    void SetTool(Tool t)
    {
        CommitText();
        _tool = t;
        foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;

        switch (t)
        {
            case Tool.Pen:
            case Tool.Highlighter:
                _ink.EditingMode = InkCanvasEditingMode.Ink;
                _ink.UseCustomCursor = false;
                break;
            case Tool.Eraser:
                _ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                _ink.UseCustomCursor = false;
                break;
            default:
                _ink.EditingMode = InkCanvasEditingMode.None;
                _ink.UseCustomCursor = true;
                _ink.Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                break;
        }
        RefreshSwatches();
        UpdateDrawingAttributes();
    }

    DrawingAttributes MakeAttributes()
    {
        bool hl = _tool == Tool.Highlighter;
        return new DrawingAttributes
        {
            Color = CurrentColor,
            Width = hl ? Math.Max(4, _size * 1.5) : _size,
            Height = hl ? Math.Max(14, _size * 4) : _size,
            IsHighlighter = hl,
            StylusTip = hl ? StylusTip.Rectangle : StylusTip.Ellipse,
            FitToCurve = _tool is Tool.Pen or Tool.Highlighter,
            IgnorePressure = true,
        };
    }

    void UpdateDrawingAttributes() => _ink.DefaultDrawingAttributes = MakeAttributes();

    static double FontSizeFor(double size) => Math.Max(14, size * 5);

    // ------------------------------------------------------------------ formas (línea, flecha, rectángulo) y texto
    void Ink_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.Text)
        {
            CommitText();
            BeginText(e.GetPosition(_textLayer));
            e.Handled = true;
            return;
        }
        if (_tool is not (Tool.Line or Tool.Arrow or Tool.Rectangle)) return;

        _start = e.GetPosition(_ink);
        _preview = new Stroke(BuildPoints(_start, _start), MakeAttributes());
        _suppress = true;
        _ink.Strokes.Add(_preview);
        _suppress = false;
        _ink.CaptureMouse();
        e.Handled = true;
    }

    void Ink_MouseMove(object sender, MouseEventArgs e)
    {
        if (_preview == null) return;
        var p = e.GetPosition(_ink);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) p = Constrain(_start, p);
        _preview.StylusPoints = BuildPoints(_start, p);
    }

    void Ink_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_preview == null) return;
        _ink.ReleaseMouseCapture();
        var stroke = _preview;
        _preview = null;

        if ((e.GetPosition(_ink) - _start).Length < 3)
        {
            // Un clic sin arrastrar: se descarta
            _suppress = true;
            _ink.Strokes.Remove(stroke);
            _suppress = false;
        }
        else
        {
            PushUndo(new StrokeChange(_ink.Strokes, new StrokeCollection { stroke }, new StrokeCollection()));
        }
        e.Handled = true;
    }

    StylusPointCollection BuildPoints(Point a, Point b)
    {
        var pts = new List<Point>();
        switch (_tool)
        {
            case Tool.Rectangle:
                pts.AddRange(new[] { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y), a });
                break;
            case Tool.Arrow:
                var v = b - a;
                double len = v.Length;
                if (len < 1) { pts.Add(a); pts.Add(b); break; }
                v /= len;
                double head = Math.Max(14, _size * 4);
                var n = new Vector(-v.Y, v.X);
                var w1 = b - v * head + n * head * 0.5;
                var w2 = b - v * head - n * head * 0.5;
                pts.AddRange(new[] { a, b, w1, b, w2 });
                break;
            default: // línea
                pts.Add(a);
                pts.Add(b);
                break;
        }
        return new StylusPointCollection(pts);
    }

    Point Constrain(Point a, Point b)
    {
        var v = b - a;
        if (_tool == Tool.Rectangle)
        {
            double m = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            return new Point(a.X + Math.Sign(v.X) * m, a.Y + Math.Sign(v.Y) * m);
        }
        double step = Math.PI / 4;
        double ang = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
        return a + new Vector(Math.Cos(ang), Math.Sin(ang)) * v.Length;
    }

    void BeginText(Point p)
    {
        _editing = new TextBox
        {
            MinWidth = 80,
            FontSize = FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_penColor),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
        };
        Canvas.SetLeft(_editing, p.X);
        Canvas.SetTop(_editing, p.Y);
        _textLayer.IsHitTestVisible = true;
        _textLayer.Children.Add(_editing);

        _editing.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { CommitText(); e.Handled = true; }
        };
        _editing.LostKeyboardFocus += (s, e) => CommitText();
        Dispatcher.InvokeAsync(() => { _editing?.Focus(); }, DispatcherPriority.Input);
    }

    void CommitText()
    {
        if (_editing == null) return;
        var tb = _editing;
        _editing = null;                 // primero, para que LostKeyboardFocus no reentre
        _textLayer.Children.Remove(tb);
        _textLayer.IsHitTestVisible = false;
        if (string.IsNullOrWhiteSpace(tb.Text)) return;

        var block = AddTextBlock(new TextNote
        {
            Text = tb.Text,
            X = Canvas.GetLeft(tb),
            Y = Canvas.GetTop(tb),
            FontSize = tb.FontSize,
            Color = ((SolidColorBrush)tb.Foreground).Color.ToString(),
        });
        PushUndo(new ElementAdd(_textLayer, block));
    }

    TextBlock AddTextBlock(TextNote n)
    {
        var block = new TextBlock
        {
            Text = n.Text,
            FontSize = n.FontSize > 0 ? n.FontSize : FontSizeFor(_size),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ParseColor(n.Color)),
            Padding = new Thickness(3, 1, 3, 1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9 },
        };
        Canvas.SetLeft(block, n.X);
        Canvas.SetTop(block, n.Y);
        _textLayer.Children.Add(block);
        return block;
    }

    static Color ParseColor(string s)
    {
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return Palette[0]; }
    }

    // ------------------------------------------------------------------ deshacer
    void PushUndo(IUndo item)
    {
        _undo.Push(item);
        MarkChanged();
    }

    void Undo()
    {
        CommitText();
        if (_undo.Count == 0) return;
        _suppress = true;
        try { _undo.Pop().Undo(); }
        finally { _suppress = false; }
        MarkChanged();
    }

    void ClearAll()
    {
        CommitText();
        var strokes = new StrokeCollection(_ink.Strokes);
        var elements = _textLayer.Children.Cast<UIElement>().ToList();
        if (strokes.Count == 0 && elements.Count == 0) return;

        _suppress = true;
        _ink.Strokes.Clear();
        _textLayer.Children.Clear();
        _suppress = false;
        PushUndo(new ClearAllChange(_ink.Strokes, strokes, _textLayer, elements));
    }

    // ------------------------------------------------------------------ zoom
    double DpiScale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    void SetZoom(double z)
    {
        z = Math.Clamp(z, 0.05, 8);
        _zoom.ScaleX = _zoom.ScaleY = z;
        _zoomText.Text = $"Zoom {Math.Round(z * DpiScale() * 100)}%";
    }

    void FitZoom()
    {
        double availW = Math.Max(100, _scroll.ActualWidth - 60);
        double availH = Math.Max(100, _scroll.ActualHeight - 60);
        double fit = Math.Min(availW / _image.PixelWidth, availH / _image.PixelHeight);
        // Nunca agrandar por encima del tamaño real en pantalla
        SetZoom(Math.Min(1 / DpiScale(), fit));
    }

    // ------------------------------------------------------------------ exportar
    BitmapSource Render()
    {
        int w = _image.PixelWidth, h = _image.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new VisualBrush(_surface)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, w, h),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    bool CopyToClipboard()
    {
        CommitText();
        var bmp = Render();

        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(png);

        // Se registra la huella de lo que se copia para que la app no
        // vuelva a abrir su propia imagen exportada.
        var exportSig = ClipboardHelper.Signature(ClipboardHelper.Decode(new MemoryStream(png.ToArray())));
        History.RegisterExport(_session, exportSig);
        png.Position = 0;

        var data = new DataObject();
        data.SetImage(bmp);
        data.SetData("PNG", png, false);

        for (int intento = 0; intento < 5; intento++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                _status.Text = "✓ Imagen anotada copiada al portapapeles";
                FlashCopied();
                if (_pendingSave) SaveSession();
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(100);
            }
        }
        _status.Text = "No se pudo acceder al portapapeles. Intenta de nuevo.";
        return false;
    }

    /// <summary>Doble parpadeo blanco con borde azul sobre la imagen.</summary>
    void FlashCopied()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 0.75,
            Duration = TimeSpan.FromMilliseconds(110),
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };
        _flash.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Cierra el editor cuando termina el parpadeo, para que se vea que se copió.</summary>
    void CloseAfterFlash()
    {
        if (_closeScheduled) return;
        _closeScheduled = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        t.Tick += (s, e) =>
        {
            t.Stop();
            if (Open.ContainsKey(_session.Meta.Id)) Close(); // pudo cerrarse a mano mientras tanto
        };
        t.Start();
    }

    void Save()
    {
        CommitText();
        var dlg = new SaveFileDialog
        {
            Filter = "Imagen PNG (*.png)|*.png|Imagen JPEG (*.jpg)|*.jpg",
            FileName = $"Captura {DateTime.Now:yyyy-MM-dd HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;

        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
        BitmapEncoder enc = ext is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render()));
        using (var fs = File.Create(dlg.FileName)) enc.Save(fs);

        _status.Text = $"✓ Guardado en {dlg.FileName}";
    }

    // ------------------------------------------------------------------ teclado
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_editing != null) return; // escribiendo texto

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(); break;
                case Key.C: if (CopyToClipboard()) CloseAfterFlash(); break;
                case Key.S: Save(); break;
                case Key.Enter: if (CopyToClipboard()) Close(); break;
                case Key.D0: case Key.NumPad0: FitZoom(); break;
                case Key.OemPlus: case Key.Add: SetZoom(_zoom.ScaleX * 1.15); break;
                case Key.OemMinus: case Key.Subtract: SetZoom(_zoom.ScaleX / 1.15); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.P: SetTool(Tool.Pen); break;
            case Key.H: SetTool(Tool.Highlighter); break;
            case Key.L: SetTool(Tool.Line); break;
            case Key.A: SetTool(Tool.Arrow); break;
            case Key.R: SetTool(Tool.Rectangle); break;
            case Key.T: SetTool(Tool.Text); break;
            case Key.E: SetTool(Tool.Eraser); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    void SetHint() =>
        _status.Text = "Ctrl+rueda: zoom · Shift: líneas a 45° y cuadrados · Ctrl+Z: deshacer · Ctrl+C: copiar y cerrar · Ctrl+S: guardar";
}
