<#
.SYNOPSIS
    Descarga PresentMon (Intel, licencia MIT) para medir los FPS del overlay.

.DESCRIPTION
    Baja el ejecutable de consola oficial desde GitHub, verifica su SHA-256 y
    lo deja en tools\presentmon\PresentMon.exe. El proyecto de la app lo copia
    junto a CtrlMonitor.exe al compilar y al publicar. Si ya está y el hash
    coincide, no vuelve a descargarlo.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\get-presentmon.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = '2.6.0'
$sha256 = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
$url = "https://github.com/GameTechDev/PresentMon/releases/download/v$version/PresentMon-$version-x64.exe"

$root = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $root 'tools\presentmon'
$target = Join-Path $targetDir 'PresentMon.exe'

if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash -eq $sha256) {
    Write-Host "PresentMon $version ya está en $target"
    return
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$temp = Join-Path $targetDir 'PresentMon.download'

Write-Host "Descargando PresentMon $version..."
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing

$hash = (Get-FileHash $temp -Algorithm SHA256).Hash
if ($hash -ne $sha256) {
    Remove-Item $temp -Force
    throw "El SHA-256 de PresentMon no coincide (esperado $sha256, obtenido $hash)."
}

Move-Item $temp $target -Force
Write-Host "PresentMon $version listo en $target"
