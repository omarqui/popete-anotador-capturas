# Prepara una version nueva para subirla a mano a la Microsoft Store desde Partner Center.
#
# Uso (desde la raiz del repo):
#   powershell -File tools\preparar-store.ps1                   # version siguiente al ultimo tag
#   powershell -File tools\preparar-store.ps1 -Version 1.2.0.0  # otra version
#
# La ultima version enviada es el tag vX.Y.Z mas alto: se crea despues de mandar a
# certificacion (README > Publicar a mano desde Partner Center). Sin -Version, sube el tercer
# numero. Exige el arbol limpio, para que el paquete salga de un commit, y que la primera linea
# de novedades.txt sea de esa version (va embebido en el .exe y se pega en Partner Center, que
# lo deja en el portapapeles al terminar); genera el .msix sin
# firmar con empaquetar-msix.ps1 -Store, guarda los commits desde el ultimo tag (materia prima
# de las novedades) y abre el Explorador con el paquete marcado, listo para arrastrarlo.
param(
    [string]$Version,
    [switch]$NoAbrir
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
# git escribe UTF-8: sin esto, PowerShell 5.1 rompe las tildes de los commits
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# Con extension: una funcion llamada como el programa (Git) se llamaba a si misma sin fin
function GitOut {
    $salida = & git.exe -C $root @args
    if ($LASTEXITCODE -ne 0) { throw "Fallo git $($args -join ' ')" }
    $salida
}

if (GitOut status --porcelain) { throw 'Hay cambios sin commitear: el paquete tiene que salir de un commit' }

# Ultima version enviada: el tag vX.Y.Z mas alto
$tags = @(GitOut tag --list 'v*' | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Substring(1) })
$ultimo = if ($tags) { $tags[-1] } else { $null }

if (-not $Version) {
    if (-not $ultimo) { throw 'No hay tags vX.Y.Z para saber la ultima version enviada: pasa -Version N.N.N.0' }
    $v = [version]$ultimo.Substring(1)
    $Version = "$($v.Major).$($v.Minor).$($v.Build + 1).0"
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "La version debe ser N.N.N.0 (la Store exige el ultimo numero en 0): $Version" }
if ($ultimo -and [version]$Version -le [version]"$($ultimo.Substring(1)).0") {
    throw "La version $Version no es mayor que la ultima enviada ($ultimo)"
}

# Las novedades viajan en el .exe (por si la Store no responde) y son las mismas de Partner Center:
# la primera linea dice la version que se arma
$corta = $Version -replace '\.0$', ''
$novedadesPath = Join-Path $root 'novedades.txt'
$novedades = [IO.File]::ReadAllText($novedadesPath, [Text.Encoding]::UTF8).Trim()
$primera = ($novedades -split "`n")[0]
if ($primera -notmatch '\d+\.\d+\.\d+' -or $Matches[0] -ne $corta) {
    throw "novedades.txt no es de la version $($corta): su primera linea es '$($primera.Trim())'. Escribe las novedades y commitealas"
}
# Partner Center admite hasta 1500 caracteres en What's new in this version
if ($novedades.Length -gt 1500) { throw "novedades.txt tiene $($novedades.Length) caracteres: la Store admite hasta 1500" }

# Commits desde la ultima enviada (sin tags, los ultimos 30)
$rango = @(if ($ultimo) { "$ultimo..HEAD" } else { '-n', '30' }) # @(): si no, un solo elemento queda como texto y @rango lo reparte letra por letra
$commits = @(GitOut log --no-merges --format=%s @rango)
if ($ultimo -and -not $commits) { throw "No hay commits nuevos desde $ultimo" }
$commit = GitOut rev-parse --short HEAD
if (-not (GitOut branch -r --contains HEAD)) { Write-Warning "El commit $commit no esta en ningun remoto: haz push antes de crear el tag" }

& "$PSScriptRoot\empaquetar-msix.ps1" -Store -Version $Version
$msix = Join-Path $root "artifacts\msix\Popete_$($Version)_x64.msix"
if (-not (Test-Path $msix)) { throw "No se genero $msix" }

$desde = if ($ultimo) { $ultimo } else { 'el principio (ultimos 30)' }
$lineas = @("Popete $Version (commit $commit). Commits desde $desde, para redactar las novedades:", '') + ($commits | ForEach-Object { "- $_" })
$notas = Join-Path $root "artifacts\msix\Popete_$($Version)_commits.txt"
[IO.File]::WriteAllLines($notas, [string[]]$lineas, (New-Object Text.UTF8Encoding $true))

$tag = "v$corta"
Set-Clipboard -Value $novedades
''
"Paquete:  $msix ($([math]::Round((Get-Item $msix).Length / 1MB, 1)) MB)"
"Commits:  $notas"
$lineas | ForEach-Object { "  $_" }
''
"Novedades (novedades.txt, ya copiadas al portapapeles):"
$novedades -split "`r?`n" | ForEach-Object { "  $_" }
''
'Siguiente: Partner Center > Popete > Start update, las novedades pegadas en Store listings y el .msix arrastrado a Packages.'
"Despues de mandar a certificacion:  git tag $tag $commit  y  git push origin $tag"
if (-not $NoAbrir) { Start-Process explorer.exe -ArgumentList "/select,`"$msix`"" }
