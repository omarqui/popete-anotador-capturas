// ----------------------------------------------------------------------------
//  Tema claro / oscuro
//
//  Los colores viven en Application.Resources con claves fijas ("Fondo",
//  "Acento"...) y la UI los usa con SetResourceReference / DynamicResource,
//  así que cambiar de tema solo reemplaza los pinceles: no se reconstruye nada.
// ----------------------------------------------------------------------------
static class Theme
{
    /// <summary>Se dispara después de aplicar un tema (para la barra de título de cada ventana).</summary>
    public static event Action Changed;

    public static bool IsDark { get; private set; }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Init()
    {
        var res = Application.Current.Resources;
        res.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(StylesXaml));
        Apply();

        // Si sigue a Windows, se actualiza cuando cambia el tema del sistema.
        // El evento llega desde otro hilo: se pasa al dispatcher de WPF.
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (AppSettings.Current.Tema == ModoTema.Sistema) Apply();
            });
        };
    }

    public static void SetMode(ModoTema mode)
    {
        AppSettings.Current.Tema = mode;
        try { AppSettings.Current.Save(); } catch { /* si no se puede guardar, igual se aplica */ }
        Apply();
    }

    static bool SystemIsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static void Apply()
    {
        IsDark = AppSettings.Current.Tema switch
        {
            ModoTema.Claro => false,
            ModoTema.Oscuro => true,
            _ => SystemIsDark(),
        };

        var r = Application.Current.Resources;
        if (IsDark)
        {
            r["Fondo"] = Solid(0x20, 0x20, 0x20);
            r["Superficie"] = Solid(0x2D, 0x2D, 0x30);
            r["Borde"] = Solid(0x3A, 0x3A, 0x3F);
            r["BordeFuerte"] = Solid(0x4D, 0x4D, 0x53);
            r["Texto"] = Solid(0xF3, 0xF3, 0xF3);
            r["TextoSecundario"] = Solid(0xB4, 0xB4, 0xB8);
            r["Icono"] = Solid(0xD6, 0xD6, 0xDA);
            r["Hover"] = Solid(0x38, 0x38, 0x3C);
            r["Seleccion"] = Solid(0x1F, 0x3A, 0x52);
            r["Acento"] = Solid(0x60, 0xCD, 0xFF);
            r["TextoSobreAcento"] = Solid(0x0B, 0x1A, 0x26);
            r["Exito"] = Solid(0x6C, 0xCB, 0x5F);
            r["Lienzo"] = DotGrid(Color.FromRgb(0x16, 0x16, 0x18), Color.FromRgb(0x2E, 0x2E, 0x33));
        }
        else
        {
            r["Fondo"] = Solid(0xF3, 0xF4, 0xF6);
            r["Superficie"] = Solid(0xFF, 0xFF, 0xFF);
            r["Borde"] = Solid(0xE1, 0xE3, 0xE8);
            r["BordeFuerte"] = Solid(0xD1, 0xD4, 0xDA);
            r["Texto"] = Solid(0x1B, 0x1B, 0x1F);
            r["TextoSecundario"] = Solid(0x5F, 0x63, 0x68);
            r["Icono"] = Solid(0x3B, 0x3F, 0x45);
            r["Hover"] = Solid(0xEB, 0xED, 0xF0);
            r["Seleccion"] = Solid(0xE5, 0xF0, 0xFB);
            r["Acento"] = Solid(0x00, 0x5F, 0xB8);
            r["TextoSobreAcento"] = Solid(0xFF, 0xFF, 0xFF);
            r["Exito"] = Solid(0x0F, 0x7B, 0x0F);
            r["Lienzo"] = DotGrid(Color.FromRgb(0xE6, 0xE8, 0xEC), Color.FromRgb(0xCD, 0xD1, 0xD8));
        }
        Changed?.Invoke();
    }

    /// <summary>Fondo, texto, fuente y barra de título de una ventana según el tema.</summary>
    public static void Attach(Window w)
    {
        w.SetResourceReference(Window.BackgroundProperty, "Fondo");
        w.SetResourceReference(Control.ForegroundProperty, "Texto");
        w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        w.FontSize = 13;

        Action titleBar = () => ApplyTitleBar(w);
        w.SourceInitialized += (s, e) => titleBar();
        Changed += titleBar;
        w.Closed += (s, e) => Changed -= titleBar;
    }

    static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    static SolidColorBrush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Fondo del lienzo: color liso con una grilla de puntos cada 16 px.</summary>
    static Brush DotGrid(Color background, Color dot)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(dot), null, new EllipseGeometry(new Point(8, 8), 1, 1)));
        var brush = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 16, 16),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }

    // Estilos de botones. Todos con Focusable=False: así un clic en la barra no
    // le quita el foco al cuadro de texto que se está escribiendo.
    const string StylesXaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

          <!-- Herramienta: ícono con fondo resaltado e indicador inferior cuando está activa -->
          <Style x:Key="EstiloHerramienta" TargetType="ToggleButton">
            <Setter Property="Width" Value="40"/>
            <Setter Property="Height" Value="36"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Icono}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                  <Grid>
                    <Border x:Name="bd" CornerRadius="6" Background="{TemplateBinding Background}"/>
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    <Border x:Name="ind" Width="16" Height="3" CornerRadius="1.5" Margin="0,0,0,3"
                            VerticalAlignment="Bottom" Background="{DynamicResource Acento}" Visibility="Collapsed"/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Seleccion}"/>
                      <Setter TargetName="ind" Property="Visibility" Value="Visible"/>
                      <Setter Property="Foreground" Value="{DynamicResource Acento}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Segmento: opción de un grupo (grosor), sin indicador -->
          <Style x:Key="EstiloSegmento" TargetType="ToggleButton">
            <Setter Property="Width" Value="34"/>
            <Setter Property="Height" Value="28"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Icono}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                  <Border x:Name="bd" CornerRadius="6" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Seleccion}"/>
                      <Setter Property="Foreground" Value="{DynamicResource Acento}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Muestra de color: círculo con anillo de acento cuando está elegida -->
          <Style x:Key="EstiloMuestra" TargetType="ToggleButton">
            <Setter Property="Width" Value="30"/>
            <Setter Property="Height" Value="30"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                  <Grid Background="Transparent">
                    <Ellipse x:Name="ring" Width="28" Height="28" StrokeThickness="2"
                             Stroke="{DynamicResource Acento}" Visibility="Hidden"/>
                    <Ellipse Width="20" Height="20" Fill="{TemplateBinding Background}"
                             Stroke="{DynamicResource BordeFuerte}" StrokeThickness="1"/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="ring" Property="Visibility" Value="Visible"/>
                      <Setter TargetName="ring" Property="Opacity" Value="0.45"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="ring" Property="Visibility" Value="Visible"/>
                      <Setter TargetName="ring" Property="Opacity" Value="1"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Botón de ícono (deshacer, limpiar, tema...) -->
          <Style x:Key="EstiloIcono" TargetType="Button">
            <Setter Property="Width" Value="36"/>
            <Setter Property="Height" Value="36"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Icono}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" CornerRadius="6" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.4"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Botón secundario: superficie con borde -->
          <Style x:Key="EstiloSecundario" TargetType="Button">
            <Setter Property="Height" Value="32"/>
            <Setter Property="Padding" Value="12,0"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="{DynamicResource Superficie}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource BordeFuerte}"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" CornerRadius="6" BorderThickness="1" Padding="{TemplateBinding Padding}"
                          Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Botón principal: color de acento -->
          <Style x:Key="EstiloPrimario" TargetType="Button">
            <Setter Property="Height" Value="32"/>
            <Setter Property="Padding" Value="12,0,10,0"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Background" Value="{DynamicResource Acento}"/>
            <Setter Property="Foreground" Value="{DynamicResource TextoSobreAcento}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" CornerRadius="6" Padding="{TemplateBinding Padding}" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Opacity" Value="0.9"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Botón plano pequeño (barra de estado) -->
          <Style x:Key="EstiloPlano" TargetType="Button">
            <Setter Property="Height" Value="24"/>
            <Setter Property="MinWidth" Value="26"/>
            <Setter Property="Padding" Value="8,0"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" CornerRadius="4" Padding="{TemplateBinding Padding}" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Miniatura del panel de capturas recientes -->
          <Style x:Key="EstiloMiniatura" TargetType="Button">
            <Setter Property="Padding" Value="6"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="{DynamicResource Superficie}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Borde}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" CornerRadius="6" Padding="{TemplateBinding Padding}"
                          Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}">
                    <ContentPresenter/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
}
