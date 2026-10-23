// ----------------------------------------------------------------------------
//  Texto del editor, con estilo simple o "etiqueta"
//
//  Foreground es siempre el color elegido (es lo que se guarda, numera y
//  recolorea). En modo etiqueta (Fondo = true):
//   - Background es un rectángulo redondeado de ese color, del tamaño exacto
//     del texto (se rehace al cambiar tamaño o color);
//   - la letra se pinta en blanco, u oscura si el color es claro, con un
//     TextEffect: así se cambia el color visible sin tocar Foreground.
//  TextBlock no deja redefinir OnRender ni OnPropertyChanged (están sellados),
//  por eso este camino.
// ----------------------------------------------------------------------------
sealed class NoteText : TextBlock
{
    static readonly Thickness PlainPadding = new Thickness(3, 1, 3, 1);
    static readonly Thickness LabelPadding = new Thickness(8, 3, 8, 4);

    bool _fondo;

    /// <summary>Cuánto movió el usuario el número del texto desde su lugar automático.</summary>
    public Vector NumberOffset { get; set; }

    public bool Fondo
    {
        get => _fondo;
        set
        {
            _fondo = value;
            Padding = value ? LabelPadding : PlainPadding;
            // El halo blanco ayuda al texto simple; en la etiqueta sobra
            Effect = value ? null : new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White, BlurRadius = 4, ShadowDepth = 0, Opacity = 0.9,
            };
            UpdateLabel();
        }
    }

    public NoteText(bool fondo)
    {
        Fondo = fondo;
        // OnPropertyChanged también está sellado: se escucha el cambio de color así
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(ForegroundProperty, typeof(TextBlock))
            .AddValueChanged(this, (s, e) => UpdateLabel());
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        UpdateLabel();
    }

    void UpdateLabel()
    {
        if (!_fondo)
        {
            Background = null;
            TextEffects = null;
            return;
        }
        var color = (Foreground as SolidColorBrush)?.Color ?? Colors.Black;

        double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight), r = Math.Min(8, h / 2);
        var shape = new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, w, h), r, r));
        Background = new DrawingBrush(shape)
        {
            Stretch = Stretch.Fill,
            Viewbox = new Rect(0, 0, w, h),
            ViewboxUnits = BrushMappingMode.Absolute,
        };

        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        var ink = luminance > 0.6 ? new SolidColorBrush(Color.FromRgb(27, 27, 31)) : Brushes.White;
        TextEffects = new TextEffectCollection { new TextEffect { Foreground = ink, PositionStart = 0, PositionCount = int.MaxValue } };
    }
}
