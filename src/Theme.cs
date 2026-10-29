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

    /// <summary>Pasa de claro a oscuro y viceversa (el botón de la barra). Deja de seguir a Windows.</summary>
    public static void Toggle() => SetMode(IsDark ? ModoTema.Claro : ModoTema.Oscuro);

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
            // Gris medio, no negro: el lienzo es más claro que las barras para que la captura destaque
            r["Fondo"] = Solid(0x26, 0x27, 0x2B);
            r["Superficie"] = Solid(0x32, 0x33, 0x38);
            r["Borde"] = Solid(0x3D, 0x3E, 0x44);
            r["BordeFuerte"] = Solid(0x50, 0x52, 0x59);
            r["Texto"] = Solid(0xF3, 0xF3, 0xF3);
            r["TextoSecundario"] = Solid(0xB8, 0xB9, 0xBE);
            r["Icono"] = Solid(0xD8, 0xD9, 0xDD);
            r["Hover"] = Solid(0x3C, 0x3D, 0x43);
            r["Seleccion"] = Solid(0x24, 0x40, 0x5A);
            r["Acento"] = Solid(0x60, 0xCD, 0xFF);
            r["TextoSobreAcento"] = Solid(0x0B, 0x1A, 0x26);
            r["Exito"] = Solid(0x6C, 0xCB, 0x5F);
            r["Error"] = Solid(0xFF, 0x99, 0xA4);
            r["BarraTitulo"] = Solid(0x1C, 0x1D, 0x20); // más oscura que Fondo: la pestaña elegida se destaca
            r["Lienzo"] = DotGrid(Color.FromRgb(0x38, 0x3A, 0x40), Color.FromRgb(0x4A, 0x4C, 0x53));
            r["FranjaLugar"] = Solid(0xFF, 0xFF, 0xFF, 0x70); // la franja de la captura en el lugar: clara en los dos temas, deja ver la pantalla
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
            r["Error"] = Solid(0xC4, 0x2B, 0x1C);
            r["BarraTitulo"] = Solid(0xE4, 0xE6, 0xEA);
            r["Lienzo"] = DotGrid(Color.FromRgb(0xE6, 0xE8, 0xEC), Color.FromRgb(0xCD, 0xD1, 0xD8));
            r["FranjaLugar"] = Solid(0xFF, 0xFF, 0xFF, 0xB0);
        }
        // El botón de cerrar la ventana se pone rojo al pasar, igual en los dos temas (como en Windows)
        r["CerrarVentana"] = Solid(0xC4, 0x2B, 0x1C);
        r["TextoSobreCerrar"] = Solid(0xFF, 0xFF, 0xFF);
        Changed?.Invoke();
    }

    /// <summary>Fondo, texto, fuente y barra de título de una ventana según el tema.</summary>
    public static void Attach(Window w)
    {
        w.SetResourceReference(Window.BackgroundProperty, "Fondo");
        w.SetResourceReference(Control.ForegroundProperty, "Texto");
        w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        w.FontSize = 13;
        w.Icon = AppInfo.WindowIcon;

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

    static SolidColorBrush Solid(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
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
            <Setter Property="Width" Value="36"/>
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
            <Setter Property="Width" Value="28"/>
            <Setter Property="Height" Value="30"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                  <Grid Background="Transparent">
                    <Ellipse x:Name="ring" Width="26" Height="26" StrokeThickness="2"
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

          <!-- Minimizar y maximizar de la barra de título propia (EditorShell): rectos, a toda la altura -->
          <Style x:Key="EstiloBotonVentana" TargetType="Button">
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource BordeFuerte}"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Cerrar la ventana: rojo al pasar -->
          <Style x:Key="EstiloCerrarVentana" TargetType="Button">
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="bd" Background="{TemplateBinding Background}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource CerrarVentana}"/>
                      <Setter Property="Foreground" Value="{DynamicResource TextoSobreCerrar}"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="bd" Property="Opacity" Value="0.85"/>
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

          <!-- Sección del menú lateral de Ajustes: fondo e indicador de acento a la izquierda cuando está elegida -->
          <Style x:Key="EstiloNavegacion" TargetType="RadioButton">
            <Setter Property="Height" Value="36"/>
            <Setter Property="Margin" Value="0,1"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                  <Grid>
                    <Border x:Name="bd" CornerRadius="6" Background="{TemplateBinding Background}"/>
                    <Border x:Name="ind" Width="3" Height="16" CornerRadius="1.5" HorizontalAlignment="Left"
                            Background="{DynamicResource Acento}" Visibility="Collapsed"/>
                    <ContentPresenter Margin="12,0" HorizontalAlignment="Left" VerticalAlignment="Center"/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Seleccion}"/>
                      <Setter TargetName="ind" Property="Visibility" Value="Visible"/>
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
          <!-- ====== Estilos implícitos: toda la app, sin tocar cada ventana ====== -->

          <!-- Tooltip: tarjeta redondeada con los colores del tema -->
          <Style TargetType="ToolTip">
            <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Background" Value="{DynamicResource Superficie}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource BordeFuerte}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                  <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="1" CornerRadius="6" Padding="8,5">
                    <ContentPresenter/>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Menú contextual y sus opciones (con marca de "elegida") -->
          <Style TargetType="ContextMenu">
            <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                  <Border Background="{DynamicResource Superficie}" BorderBrush="{DynamicResource BordeFuerte}"
                          BorderThickness="1" CornerRadius="8" Padding="4">
                    <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="MenuItem">
            <Setter Property="Foreground" Value="{DynamicResource Texto}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="MenuItem">
                  <Border x:Name="bd" Background="Transparent" CornerRadius="4" Padding="6,6,16,6">
                    <Grid>
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="24"/>
                        <ColumnDefinition Width="*"/>
                      </Grid.ColumnDefinitions>
                      <Path x:Name="check" Data="M3 8 L7 12 L14 4" Stroke="{DynamicResource Acento}" StrokeThickness="2"
                            StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                            VerticalAlignment="Center" Visibility="Hidden"/>
                      <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center"/>
                    </Grid>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True">
                      <Setter TargetName="bd" Property="Background" Value="{DynamicResource Hover}"/>
                    </Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter TargetName="check" Property="Visibility" Value="Visible"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <!-- Barras de desplazamiento finas, sin flechas -->
          <ControlTemplate x:Key="PulgarBarra" TargetType="Thumb">
            <Border x:Name="t" CornerRadius="4" Margin="2" Background="{DynamicResource BordeFuerte}"/>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="t" Property="Background" Value="{DynamicResource TextoSecundario}"/>
              </Trigger>
              <Trigger Property="IsDragging" Value="True">
                <Setter TargetName="t" Property="Background" Value="{DynamicResource TextoSecundario}"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
          <Style TargetType="ScrollBar">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Width" Value="10"/>
            <Setter Property="MinWidth" Value="10"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                  <Grid Background="{TemplateBinding Background}">
                    <Track x:Name="PART_Track" IsDirectionReversed="True">
                      <Track.DecreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/>
                      </Track.DecreaseRepeatButton>
                      <Track.Thumb>
                        <Thumb Template="{StaticResource PulgarBarra}"/>
                      </Track.Thumb>
                      <Track.IncreaseRepeatButton>
                        <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/>
                      </Track.IncreaseRepeatButton>
                    </Track>
                  </Grid>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto"/>
                <Setter Property="MinWidth" Value="0"/>
                <Setter Property="Height" Value="10"/>
                <Setter Property="MinHeight" Value="10"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                      <Grid Background="{TemplateBinding Background}">
                        <Track x:Name="PART_Track">
                          <Track.DecreaseRepeatButton>
                            <RepeatButton Command="ScrollBar.PageLeftCommand" Opacity="0" Focusable="False"/>
                          </Track.DecreaseRepeatButton>
                          <Track.Thumb>
                            <Thumb Template="{StaticResource PulgarBarra}"/>
                          </Track.Thumb>
                          <Track.IncreaseRepeatButton>
                            <RepeatButton Command="ScrollBar.PageRightCommand" Opacity="0" Focusable="False"/>
                          </Track.IncreaseRepeatButton>
                        </Track>
                      </Grid>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Trigger>
            </Style.Triggers>
          </Style>

          <!-- ScrollViewer sin el cuadrado gris de la esquina entre las dos barras -->
          <Style TargetType="ScrollViewer">
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ScrollViewer">
                  <Grid Background="{TemplateBinding Background}">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="*"/>
                      <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                      <RowDefinition Height="*"/>
                      <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Margin="{TemplateBinding Padding}"
                                            CanContentScroll="{TemplateBinding CanContentScroll}"/>
                    <ScrollBar x:Name="PART_VerticalScrollBar" Grid.Column="1"
                               Value="{TemplateBinding VerticalOffset}" Maximum="{TemplateBinding ScrollableHeight}"
                               ViewportSize="{TemplateBinding ViewportHeight}"
                               Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}"/>
                    <ScrollBar x:Name="PART_HorizontalScrollBar" Grid.Row="1" Orientation="Horizontal"
                               Value="{TemplateBinding HorizontalOffset}" Maximum="{TemplateBinding ScrollableWidth}"
                               ViewportSize="{TemplateBinding ViewportWidth}"
                               Visibility="{TemplateBinding ComputedHorizontalScrollBarVisibility}"/>
                  </Grid>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
}
