# Genera assets\popete.ico a partir de assets\popete.svg (y, para 16-24 px,
# de assets\popete-pequeno.svg, una version simplificada que se lee mejor en la bandeja),
# y los logos del paquete MSIX en packaging\Assets (ver tools\empaquetar-msix.ps1).
#
# Uso (desde la raiz del repo):  powershell -STA -File tools\generar-icono.ps1
#
# Dibuja el SVG con WPF, sin dependencias externas. Solo entiende las formas
# que usa el logo: rect, circle, ellipse, path, line y polyline, con fill,
# stroke, stroke-width, stroke-linecap y stroke-linejoin (sin transformaciones,
# gradientes ni texto). El .ico lleva cada tamano como PNG.
param(
    [string]$Svg = "$PSScriptRoot\..\assets\popete.svg",
    [string]$SmallSvg = "$PSScriptRoot\..\assets\popete-pequeno.svg",
    [int]$SmallMax = 24,   # hasta este tamano se usa la version simplificada
    [string]$Ico = "$PSScriptRoot\..\assets\popete.ico",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 256),
    [string]$PreviewDir,  # opcional: guarda tambien un PNG por tamano
    [string]$MsixAssets = "$PSScriptRoot\..\packaging\Assets"  # logos del paquete MSIX
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

function Brush($value) {
    if (-not $value -or $value -eq 'none') { return $null }
    $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($value))
    $b.Freeze(); return $b
}

function Num($node, $name, $default = 0) {
    $v = $node.GetAttribute($name)
    if ($v) { return [double]::Parse($v, [Globalization.CultureInfo]::InvariantCulture) } else { return $default }
}

# Lee un SVG y devuelve su dibujo WPF y su viewBox
function LoadSvg($path) {
    [xml]$doc = Get-Content -Raw -Encoding UTF8 (Resolve-Path $path)
    $root = $doc.DocumentElement
    $vb = ($root.GetAttribute('viewBox') -split '[\s,]+') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }

    $group = New-Object System.Windows.Media.DrawingGroup
    foreach ($n in $root.ChildNodes) {
        if ($n.NodeType -ne 'Element') { continue }
        $g = switch ($n.LocalName) {
            'rect' {
                $rx = Num $n 'rx'; $ry = Num $n 'ry' $rx
                New-Object System.Windows.Media.RectangleGeometry (New-Object System.Windows.Rect (Num $n 'x'), (Num $n 'y'), (Num $n 'width'), (Num $n 'height')), $rx, $ry
            }
            'circle' { $r = Num $n 'r'; New-Object System.Windows.Media.EllipseGeometry (New-Object System.Windows.Point (Num $n 'cx'), (Num $n 'cy')), $r, $r }
            'ellipse' { New-Object System.Windows.Media.EllipseGeometry (New-Object System.Windows.Point (Num $n 'cx'), (Num $n 'cy')), (Num $n 'rx'), (Num $n 'ry') }
            'line' { New-Object System.Windows.Media.LineGeometry (New-Object System.Windows.Point (Num $n 'x1'), (Num $n 'y1')), (New-Object System.Windows.Point (Num $n 'x2'), (Num $n 'y2')) }
            'polyline' { [System.Windows.Media.Geometry]::Parse('M' + ($n.GetAttribute('points').Trim() -replace '\s+', ' L')) }
            'path' { [System.Windows.Media.Geometry]::Parse($n.GetAttribute('d')) }
            default { throw "Elemento SVG no soportado: <$($n.LocalName)>" }
        }
        $fill = Brush ($(if ($n.HasAttribute('fill')) { $n.GetAttribute('fill') } elseif ($n.LocalName -in 'line', 'polyline') { 'none' } else { '#000000' }))
        $pen = $null
        $stroke = Brush $n.GetAttribute('stroke')
        if ($stroke) {
            $pen = New-Object System.Windows.Media.Pen $stroke, (Num $n 'stroke-width' 1)
            $cap = switch ($n.GetAttribute('stroke-linecap')) { 'round' { 'Round' } 'square' { 'Square' } default { 'Flat' } }
            $pen.StartLineCap = $cap; $pen.EndLineCap = $cap
            $pen.LineJoin = switch ($n.GetAttribute('stroke-linejoin')) { 'round' { 'Round' } 'bevel' { 'Bevel' } default { 'Miter' } }
        }
        $group.Children.Add((New-Object System.Windows.Media.GeometryDrawing $fill, $pen, $g))
    }
    $group.Freeze()
    return @{ Drawing = $group; ViewBox = $vb }
}

function RenderPng([int]$size, $svg) { return , (RenderCentered $size $size $size $svg) }

# Lienzo de $width x $height transparente con el logo de $icon px centrado
function RenderCentered([int]$width, [int]$height, [int]$icon, $svg) {
    $size = $icon
    $vb = $svg.ViewBox
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ([math]::Floor(($width - $icon) / 2)), ([math]::Floor(($height - $icon) / 2))))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $vb[2]), ($size / $vb[3])))
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$vb[0]), (-$vb[1])))
    $dc.DrawDrawing($svg.Drawing)
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $width, $height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$big = LoadSvg $Svg
$small = if ($SmallSvg -and (Test-Path $SmallSvg)) { LoadSvg $SmallSvg } else { $big }

$images = foreach ($s in $Sizes) { , (RenderPng $s ($(if ($s -le $SmallMax) { $small } else { $big }))) }
if ($PreviewDir) {
    New-Item -ItemType Directory -Force $PreviewDir | Out-Null
    for ($i = 0; $i -lt $Sizes.Count; $i++) { [IO.File]::WriteAllBytes((Join-Path $PreviewDir "popete-$($Sizes[$i]).png"), $images[$i]) }
}

# ICO: cabecera + una entrada por tamano + los PNG
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$Sizes.Count)
$offset = 6 + 16 * $Sizes.Count
for ($i = 0; $i -lt $Sizes.Count; $i++) {
    $s = $Sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 se escribe como 0
    $w.Write([byte]0); $w.Write([byte]0)                      # paleta, reservado
    $w.Write([uint16]1); $w.Write([uint16]32)                 # planos, bits por pixel
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Ico), $out.ToArray())
"Generado $([IO.Path]::GetFullPath($Ico)) ($($Sizes -join ', ') px, $($out.Length) bytes)"

# Logos del MSIX, con los calificadores que resuelve resources.pri (makepri):
# scale-* para mosaicos y Store, targetsize-* (y sus variantes sin placa) para barra de tareas e Inicio.
if ($MsixAssets) {
    $dir = [IO.Path]::GetFullPath($MsixAssets)
    New-Item -ItemType Directory -Force $dir | Out-Null
    function Save([string]$name, [int]$w, [int]$h, [int]$icon) {
        $svg = if ($icon -le $SmallMax) { $small } else { $big }
        [IO.File]::WriteAllBytes((Join-Path $dir $name), (RenderCentered $w $h $icon $svg))
    }
    foreach ($scale in 100, 200, 400) {
        $f = $scale / 100
        Save "Square44x44Logo.scale-$scale.png" (44 * $f) (44 * $f) (44 * $f)
        Save "Square150x150Logo.scale-$scale.png" (150 * $f) (150 * $f) (100 * $f)   # con margen, como los mosaicos de Windows
        Save "Wide310x150Logo.scale-$scale.png" (310 * $f) (150 * $f) (100 * $f)
        Save "StoreLogo.scale-$scale.png" (50 * $f) (50 * $f) (50 * $f)
    }
    foreach ($t in 16, 20, 24, 32, 40, 48, 64, 256) {
        Save "Square44x44Logo.targetsize-$t.png" $t $t $t
        Save "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t $t
        Save "Square44x44Logo.targetsize-${t}_altform-lightunplated.png" $t $t $t
    }
    "Generados los logos del MSIX en $dir"
}
