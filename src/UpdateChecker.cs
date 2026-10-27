// ----------------------------------------------------------------------------
//  Versión nueva (solo instalada desde la Store)
//
//  Popete vive en la bandeja y arranca con Windows: casi nunca está cerrada, y la
//  Store no actualiza una app mientras corre. Por eso le pregunta a la Store
//  (StoreContext) al rato de arrancar y cada 6 horas (si AppSettings.BuscarActualizaciones).
//  Si hay una versión nueva, muestra un globo en la bandeja (una vez por sesión) y
//  "Actualizar Popete" arriba en su menú. Ajustes › Actualizaciones muestra el estado
//  (State, Changed) y permite buscar a mano (CheckNow). Al instalar, cierra los
//  editores (guardan lo pendiente), descarga e instala sin más preguntas, y Windows
//  vuelve a abrir Popete (RegisterApplicationRestart). Si la Store no deja instalar
//  así, abre la página de Popete en la Store, donde está el botón Actualizar.
// ----------------------------------------------------------------------------
enum UpdateState
{
    /// <summary>No es el MSIX de la Store (compilada desde el código): no hay a quién preguntar.</summary>
    NotFromStore,
    NotChecked,
    Checking,
    UpToDate,
    Available,
    /// <summary>La Store no respondió (sin conexión o paquete de prueba).</summary>
    Failed,
    Installing,
}

sealed class UpdateChecker
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterApplicationRestart(string commandLine, int flags);

    static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>El de la bandeja (lo crea CaptureHost.CreateTray); null antes.</summary>
    public static UpdateChecker Current { get; private set; }

    /// <summary>Cada cambio de <see cref="State"/> (Ajustes lo muestra).</summary>
    public static event Action Changed;

    public UpdateState State { get; private set; } = AppInfo.IsPackaged ? UpdateState.NotChecked : UpdateState.NotFromStore;
    public DateTime? LastCheck { get; private set; }

    /// <summary>La versión nueva («1.1.4»), con <see cref="UpdateState.Available"/>.</summary>
    public string AvailableVersion { get; private set; }

    readonly System.Windows.Forms.NotifyIcon _tray;
    readonly System.Windows.Forms.ToolStripMenuItem _item;
    readonly System.Windows.Forms.ToolStripSeparator _separator;
    readonly DispatcherTimer _timer;
    Windows.Services.Store.StoreContext _store;
    IReadOnlyList<Windows.Services.Store.StorePackageUpdate> _updates;
    bool _notified;      // el globo sale una vez por sesión; la opción del menú queda
    bool _balloonIsOurs; // el clic en el globo es para actualizar (hay otros globos)

    /// <summary>Agrega su opción (oculta hasta que haya versión nueva) arriba del menú de la bandeja.</summary>
    public UpdateChecker(System.Windows.Forms.NotifyIcon tray, System.Windows.Forms.ContextMenuStrip menu)
    {
        Current = this;
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
            if (AppSettings.Current.BuscarActualizaciones) await Check(manual: false);
        };
        if (AppInfo.IsPackaged) _timer.Start(); // el .exe suelto no viene de la Store
    }

    Windows.Services.Store.StoreContext Store() => _store ??= Windows.Services.Store.StoreContext.GetDefault();

    void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    /// <summary>«Buscar actualizaciones» en Ajustes: sin globo (ya se está mirando).</summary>
    public System.Threading.Tasks.Task CheckNow() => Check(manual: true);

    async System.Threading.Tasks.Task Check(bool manual)
    {
        if (!AppInfo.IsPackaged || State is UpdateState.Checking or UpdateState.Installing) return;
        SetState(UpdateState.Checking);
        try
        {
            var updates = await Store().GetAppAndOptionalStorePackageUpdatesAsync().AsTask();
            LastCheck = DateTime.Now;
            if (updates.Count == 0)
            {
                _updates = null;
                _item.Available = _separator.Available = false;
                SetState(UpdateState.UpToDate);
                return;
            }
            _updates = updates;
            var v = updates[0].Package.Id.Version;
            AvailableVersion = $"{v.Major}.{v.Minor}.{v.Build}";
            _item.Available = _separator.Available = true;
            SetState(UpdateState.Available);
            if (_notified || manual) return;
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
            LastCheck = DateTime.Now;
            SetState(UpdateState.Failed);
        }
    }

    public async void Install()
    {
        if (State == UpdateState.Installing || _updates == null) return;
        SetState(UpdateState.Installing);
        try
        {
            var store = Store();
            if (store.CanSilentlyDownloadStorePackageUpdates)
            {
                // La ventana del editor se cierra antes: guarda lo pendiente y sus pestañas vuelven después
                EditorShell.CloseWindow();
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
        SetState(UpdateState.Available);
        OpenStorePage();
    }

    /// <summary>La página de Popete en la Store (por el nombre de familia del paquete).</summary>
    public static void OpenStorePage()
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
