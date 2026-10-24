// ----------------------------------------------------------------------------
//  Aviso de versión nueva (solo instalada desde la Store)
//
//  Popete vive en la bandeja y arranca con Windows: casi nunca está cerrada, y la
//  Store no actualiza una app mientras corre. Por eso le pregunta a la Store
//  (StoreContext) al rato de arrancar y cada 6 horas. Si hay una versión nueva,
//  muestra un globo en la bandeja (una vez por sesión) y "Actualizar Popete" arriba
//  en su menú. Al elegirlo, cierra los editores (guardan lo pendiente), descarga e
//  instala sin más preguntas, y Windows vuelve a abrir Popete
//  (RegisterApplicationRestart). Si la Store no deja instalar así, abre la página
//  de Popete en la Store, donde está el botón Actualizar.
// ----------------------------------------------------------------------------
sealed class UpdateChecker
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterApplicationRestart(string commandLine, int flags);

    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    readonly System.Windows.Forms.NotifyIcon _tray;
    readonly System.Windows.Forms.ToolStripMenuItem _item;
    readonly System.Windows.Forms.ToolStripSeparator _separator;
    readonly DispatcherTimer _timer;
    Windows.Services.Store.StoreContext _store;
    IReadOnlyList<Windows.Services.Store.StorePackageUpdate> _updates;
    bool _notified;      // el globo sale una vez por sesión; la opción del menú queda
    bool _balloonIsOurs; // el clic en el globo es para actualizar (hay otros globos)
    bool _installing;

    /// <summary>Agrega su opción (oculta hasta que haya versión nueva) arriba del menú de la bandeja.</summary>
    public UpdateChecker(System.Windows.Forms.NotifyIcon tray, System.Windows.Forms.ContextMenuStrip menu)
    {
        _tray = tray;
        _item = new System.Windows.Forms.ToolStripMenuItem("Actualizar Popete", null, (s, e) => Install())
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
            Available = false,
        };
        _separator = new System.Windows.Forms.ToolStripSeparator { Available = false };
        menu.Items.Insert(0, _item);
        menu.Items.Insert(1, _separator);

        _tray.BalloonTipClicked += (s, e) => { if (_balloonIsOurs) Install(); };
        _tray.BalloonTipClosed += (s, e) => _balloonIsOurs = false;

        _timer = new DispatcherTimer { Interval = FirstCheck };
        _timer.Tick += async (s, e) =>
        {
            _timer.Interval = Interval;
            await Check();
        };
        if (AppInfo.IsPackaged) _timer.Start(); // el .exe suelto no viene de la Store
    }

    Windows.Services.Store.StoreContext Store() => _store ??= Windows.Services.Store.StoreContext.GetDefault();

    async System.Threading.Tasks.Task Check()
    {
        if (_installing) return;
        try
        {
            var updates = await Store().GetAppAndOptionalStorePackageUpdatesAsync().AsTask();
            if (updates.Count == 0) return;
            _updates = updates;
            _item.Available = _separator.Available = true;
            if (_notified) return;
            _notified = true;
            _balloonIsOurs = true;
            _tray.ShowBalloonTip(10000, "Hay una versión nueva de Popete",
                "Haz clic aquí para actualizar; Popete se vuelve a abrir solo. También está en el menú de la bandeja.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
        catch
        {
            // Sin conexión, o un paquete que no se instaló desde la Store (de prueba): se
            // vuelve a intentar en la próxima vuelta, sin llenar errores.log
        }
    }

    async void Install()
    {
        if (_installing || _updates == null) return;
        _installing = true;
        try
        {
            var store = Store();
            if (store.CanSilentlyDownloadStorePackageUpdates)
            {
                // Los editores se cierran antes: al cerrarse guardan lo pendiente
                foreach (var w in EditorWindow.Open.Values.ToList()) w.Close();
                RegisterApplicationRestart(null, 0); // que Windows vuelva a abrir Popete después de instalar
                _tray.ShowBalloonTip(3000, AppInfo.Name, "Descargando la versión nueva…", System.Windows.Forms.ToolTipIcon.Info);
                var result = await store.TrySilentDownloadAndInstallStorePackageUpdatesAsync(_updates).AsTask();
                if (result.OverallState == Windows.Services.Store.StorePackageUpdateState.Completed) return; // Windows cierra Popete
            }
        }
        catch (Exception ex)
        {
            AppInfo.LogError(ex);
        }
        _installing = false;
        OpenStorePage();
    }

    /// <summary>La página de Popete en la Store (por el nombre de familia del paquete).</summary>
    static void OpenStorePage()
    {
        try
        {
            var pfn = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://pdp/?PFN=" + pfn) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppInfo.LogError(ex);
        }
    }
}
