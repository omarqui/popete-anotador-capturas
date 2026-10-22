# Genera assets\popete.ico a partir de assets\popete.svg.
#
# Uso (desde la raíz del repo):  powershell -STA -File tools\generar-icono.ps1
#
# Dibuja el SVG con WPF, sin dependencias externas. Solo entiende las formas
# que usa el logo: rect, circle, ellipse, path, line y polyline, con fill,
# stroke, stroke-width, stroke-linecap y stroke-linejoin (sin transformaciones,
# gradientes ni texto). El .ico lleva cada tamaño como PNG.
param(
    [string]$Svg = "$PSScriptRoot\..\assets\popete.svg",
    [string]$Ico = "$PSScriptRoot\..\assets\popete.ico",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 256),
    [string]$PreviewDir   # opcional: guarda también un PNG por tamaño
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

[xml]$doc = Get-Content -Raw -Encoding UTF8 (Resolve-Path $Svg)
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

function RenderPng([int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $vb[2]), ($size / $vb[3])))
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$vb[0]), (-$vb[1])))
    $dc.DrawDrawing($group)
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

$images = foreach ($s in $Sizes) { , (RenderPng $s) }
if ($PreviewDir) {
    New-Item -ItemType Directory -Force $PreviewDir | Out-Null
    for ($i = 0; $i -lt $Sizes.Count; $i++) { [IO.File]::WriteAllBytes((Join-Path $PreviewDir "popete-$($Sizes[$i]).png"), $images[$i]) }
}

# ICO: cabecera + una entrada por tamaño + los PNG
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$Sizes.Count)
$offset = 6 + 16 * $Sizes.Count
for ($i = 0; $i -lt $Sizes.Count; $i++) {
    $s = $Sizes[$i]; $len = $images[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 se escribe como 0
    $w.Write([byte]0); $w.Write([byte]0)                      # paleta, reservado
    $w.Write([uint16]1); $w.Write([uint16]32)                 # planos, bits por píxel
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Ico), $out.ToArray())
"Generado $([IO.Path]::GetFullPath($Ico)) ($($Sizes -join ', ') px, $($out.Length) bytes)"
