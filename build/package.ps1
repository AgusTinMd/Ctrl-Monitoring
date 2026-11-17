<#
.SYNOPSIS
    Empaqueta Ctrl Monitor para distribuir: exe unico autocontenido, zip
    portable e instalador de Inno Setup.

.DESCRIPTION
    Lo usan tanto la compilacion local como el workflow de GitHub Actions,
    asi ambos generan exactamente los mismos archivos en artifacts/:

      CtrlMonitor-<version>-win-x64-portable.zip
      CtrlMonitorSetup-<version>.exe   (si Inno Setup esta instalado)
      SHA256SUMS.txt

    El exe incluye el runtime de .NET: no hace falta instalar .NET aparte.
    PawnIO sigue siendo un requisito externo (driver firmado, instalador propio).

.PARAMETER Version
    Version a publicar (por ejemplo 2.7.0 o v2.7.0). Si se omite, se usa el
    ultimo tag vX.Y.Z del repositorio. El csproj queda fijo en 0.0.0: la
    version real siempre sale del tag.

.PARAMETER Iscc
    Ruta a ISCC.exe. Si se omite, se busca en el PATH y en las rutas de
    instalacion habituales de Inno Setup 6.

.PARAMETER SkipTests
    No corre los tests antes de empaquetar.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\package.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\package.ps1 -Version 2.7.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Iscc,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\CtrlMonitor.App\CtrlMonitor.App.csproj'
$solution = Join-Path $root 'CtrlMonitor.slnx'
$installerScript = Join-Path $root 'installer\CtrlMonitor.iss'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish'

function Invoke-Step([string]$title, [scriptblock]$action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $action
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "Fallo: $title (codigo $LASTEXITCODE)"
    }
}

if (-not $Version) {
    # Los tags viven en los merges de main, que no estan en la historia de
    # DEV ni de sus ramas: por eso se toma el tag de version mas alto del
    # repositorio en lugar de usar git describe.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $tag = git -C $root tag --list 'v*.*.*' --sort=-v:refname 2>$null | Select-Object -First 1
    $ErrorActionPreference = $previousPreference

    if (-not $tag) {
        throw 'No hay ningun tag vX.Y.Z en el repositorio. Pasar la version con -Version 1.2.3.'
    }

    $Version = "$tag".Trim()
    Write-Host "Version tomada del tag $Version"
}

$Version = $Version.TrimStart('v', 'V')

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version invalida '$Version'. Usar el formato 1.2.3."
}

Write-Host "Ctrl Monitor $Version" -ForegroundColor Green

if (Test-Path $artifacts) {
    Remove-Item -Recurse -Force $artifacts
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

if (-not $SkipTests) {
    Invoke-Step 'Tests' {
        dotnet test $solution -c Release --nologo
    }
}

Invoke-Step 'Publicar exe unico autocontenido (win-x64)' {
    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:Version=$Version `
        -o $publishDir `
        --nologo
}

$exe = Join-Path $publishDir 'CtrlMonitor.exe'
if (-not (Test-Path $exe)) {
    throw "No se genero $exe"
}

$zip = Join-Path $artifacts "CtrlMonitor-$Version-win-x64-portable.zip"
Invoke-Step 'Zip portable' {
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal
}

if (-not $Iscc) {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $Iscc = $candidates | Select-Object -First 1
}

if ($Iscc) {
    Invoke-Step 'Instalador (Inno Setup)' {
        & $Iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" $installerScript
    }
}
else {
    Write-Warning 'No se encontro Inno Setup 6 (ISCC.exe): se omite el instalador. Instalarlo con: winget install JRSoftware.InnoSetup'
}

$checksums = Join-Path $artifacts 'SHA256SUMS.txt'
Get-ChildItem $artifacts -File |
    Where-Object { $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } |
    Set-Content -Path $checksums -Encoding ascii

Write-Host ''
Write-Host 'Archivos generados:' -ForegroundColor Green
Get-ChildItem $artifacts -File | ForEach-Object {
    '  {0,-45} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB)
}
