# Genera el paquete MSIX de Popete en artifacts\msix\.
#
# Uso (desde la raiz del repo):
#   Prueba local (firmado con un certificado propio):
#     powershell -File tools\empaquetar-msix.ps1
#   Para subir a la Microsoft Store (sin firmar: la Store lo firma). La identidad de
#   Partner Center ya esta abajo como valor por defecto; solo cambia la version:
#     powershell -File tools\empaquetar-msix.ps1 -Store -Version 1.0.0.0
#
# Pasos: dotnet publish (self-contained) -> carpeta del paquete con el manifiesto
# (packaging\AppxManifest.xml) y los logos (packaging\Assets) -> resources.pri (makepri)
# -> .msix (makeappx) -> firma de prueba (signtool), salvo con -Store.
#
# makeappx, makepri y signtool salen del Windows SDK si esta instalado; si no, se
# descarga una vez el paquete NuGet oficial Microsoft.Windows.SDK.BuildTools en
# artifacts\sdk-buildtools.
param(
    [string]$IdentityName = 'Popete.Prueba',
    [string]$Publisher = 'CN=Popete Prueba',
    [string]$PublisherDisplayName = 'Popete (prueba)',
    [string]$Version = '1.0.0.0',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [switch]$Store
)
$ErrorActionPreference = 'Stop'

# Identidad de Partner Center (Producto > Identidad del producto), salvo que se pase otra
if ($Store) {
    if (-not $PSBoundParameters.ContainsKey('IdentityName')) { $IdentityName = 'OmarquiAybar.Popeteanotadordecapturas' }
    if (-not $PSBoundParameters.ContainsKey('Publisher')) { $Publisher = 'CN=EC098364-9CE9-46BE-B0B5-F8B4753B53CA' }
    if (-not $PSBoundParameters.ContainsKey('PublisherDisplayName')) { $PublisherDisplayName = 'Omarqui Aybar' }
}
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$out = Join-Path $root 'artifacts\msix'

# La Store exige cuatro numeros y el ultimo en 0
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "La version debe ser N.N.N.0 (la Store exige el ultimo numero en 0): $Version" }

# Corre una herramienta; su salida solo se muestra si falla
function Run([string]$exe, [string[]]$arguments) {
    $log = & $exe @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $log | ForEach-Object { Write-Host $_ }
        throw "Fallo $([IO.Path]::GetFileName($exe)) (codigo $LASTEXITCODE)"
    }
}

# ---------------------------------------------------------------- herramientas del SDK
function Find-SdkTools {
    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kits) {
        $best = Get-ChildItem $kits -Directory |
            Where-Object { $_.Name -match '^\d+\.' -and (Test-Path "$($_.FullName)\x64\makeappx.exe") } |
            Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($best) { return "$($best.FullName)\x64" }
    }
    $cache = Join-Path $root 'artifacts\sdk-buildtools'
    $found = Get-ChildItem $cache -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $found) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $feed = 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools'
        $ver = (Invoke-RestMethod "$feed/index.json").versions | Where-Object { $_ -notmatch '-' } |
            Sort-Object { [version]$_ } | Select-Object -Last 1
        Write-Host "Descargando Microsoft.Windows.SDK.BuildTools $ver (makeappx, makepri, signtool)..."
        New-Item -ItemType Directory -Force $cache | Out-Null
        $zip = Join-Path $cache 'buildtools.zip'
        Invoke-WebRequest "$feed/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg" -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath (Join-Path $cache $ver) -Force
        Remove-Item $zip
        $found = Get-ChildItem $cache -Recurse -Filter makeappx.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
        if (-not $found) { throw 'No se encontro makeappx.exe en Microsoft.Windows.SDK.BuildTools' }
    }
    return $found.DirectoryName
}
$sdk = Find-SdkTools
$makeappx = Join-Path $sdk 'makeappx.exe'
$makepri = Join-Path $sdk 'makepri.exe'
$signtool = Join-Path $sdk 'signtool.exe'

# ---------------------------------------------------------------- publicar
$app = Join-Path $out "app-$Arch"
$stage = Join-Path $out "paquete-$Arch"
$priRoot = Join-Path $out "pri-$Arch"
foreach ($d in $app, $stage, $priRoot) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }

Write-Host "Publicando Popete ($Arch, self-contained)..."
Run 'dotnet' @('publish', "$root\src", '-c', 'Release', '-r', "win-$Arch", '--self-contained', 'true', '-o', $app, '-nologo', '-v', 'quiet')

# ---------------------------------------------------------------- carpeta del paquete
New-Item -ItemType Directory -Force $stage, $priRoot | Out-Null
Copy-Item "$app\*" $stage -Recurse
Copy-Item "$root\packaging\Assets" $stage -Recurse
Copy-Item "$root\packaging\Assets" $priRoot -Recurse

$manifest = Get-Content -Raw -Encoding UTF8 "$root\packaging\AppxManifest.xml"
$values = @{
    '$IDENTITY_NAME$'          = $IdentityName
    '$PUBLISHER$'              = $Publisher
    '$PUBLISHER_DISPLAY_NAME$' = $PublisherDisplayName
    '$VERSION$'                = $Version
    '$ARCH$'                   = $Arch
}
foreach ($k in $values.Keys) { $manifest = $manifest.Replace($k, [Security.SecurityElement]::Escape($values[$k])) }
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText("$stage\AppxManifest.xml", $manifest, $utf8)
[IO.File]::WriteAllText("$priRoot\AppxManifest.xml", $manifest, $utf8)

# resources.pri: resuelve Assets\X.png a X.scale-200.png, X.targetsize-24.png...
# Se indexa una copia solo con los logos, para no meter las DLL como recursos.
$priConfig = Join-Path $out 'priconfig.xml'
Run $makepri @('createconfig', '/cf', $priConfig, '/dq', 'es-ES', '/pv', '10.0.0', '/o')
# Sin <packaging>: si no, makepri parte los recursos por escala (resources.scale-200.pri...),
# que solo sirven en un bundle; en un .msix suelto los logos grandes no se encontrarian.
[xml]$cfg = Get-Content -Raw $priConfig
$cfg.SelectNodes('//packaging') | ForEach-Object { [void]$_.ParentNode.RemoveChild($_) }
$cfg.Save($priConfig)
Run $makepri @('new', '/pr', $priRoot, '/cf', $priConfig, '/mn', "$priRoot\AppxManifest.xml", '/of', "$stage\resources.pri", '/o')

# ---------------------------------------------------------------- .msix
$suffix = if ($Store) { '' } else { '-prueba' }
$msix = Join-Path $out "Popete_$($Version)_$Arch$suffix.msix"
Run $makeappx @('pack', '/d', $stage, '/p', $msix, '/o')

if ($Store) {
    Write-Host ""
    Write-Host "Listo para Partner Center (sin firmar; la Store lo firma): $msix"
    return
}

# ---------------------------------------------------------------- firma de prueba
# Certificado propio en CurrentUser\My con el mismo Subject que el Publisher del manifiesto.
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creando certificado de prueba '$Publisher' en CurrentUser\My..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature `
        -FriendlyName 'Popete: firma de prueba del MSIX' -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
}
$cer = Join-Path $out 'Popete-prueba.cer'
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Run $signtool @('sign', '/fd', 'SHA256', '/sha1', $cert.Thumbprint, '/s', 'My', '/q', $msix)

Write-Host ""
Write-Host "Paquete de prueba firmado: $msix"
Write-Host "Para instalarlo:"
Write-Host "  1. Una vez, como administrador, confiar en el certificado:"
Write-Host "     Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host "  2. Cerrar Popete si esta abierto (bandeja > Salir) e instalar:"
Write-Host "     Add-AppxPackage '$msix'"
