#requires -Version 5.1
<#
.SYNOPSIS
    Publica RelojChecador.WPF como un ejecutable autocontenido de un solo archivo para
    Windows x86 (32 bits), listo para que Inno Setup lo empaquete.

.DESCRIPTION
    win-x86, no win-x64: zkemkeeper.dll (SDK real de ZKTeco, ver
    third-party/zkteco-sdk/README.md) es un COM server de 32 bits — un proceso .NET de
    64 bits no puede activarlo por interop.

    Se ejecuta EN WINDOWS (o con el SDK de .NET instalado apuntando a win-x86).
    Genera la carpeta installer/publish con RelojChecador.WPF.exe y sus dependencias.
    No incluye el runtime de .NET del sistema (self-contained) para que el instalador
    no dependa de que el equipo destino ya tenga .NET 10 instalado.

.PARAMETER Configuration
    Configuración de compilación. Por defecto Release.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/RelojChecador.WPF/RelojChecador.WPF.csproj"
$outputDir = Join-Path $PSScriptRoot "publish"

if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}

Write-Host "Publicando $project (win-x86, self-contained, un solo archivo)..." -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r win-x86 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falló con código $LASTEXITCODE"
}

Write-Host "Listo. Ejecutable en: $outputDir\RelojChecador.WPF.exe" -ForegroundColor Green
Write-Host "Siguiente paso: compilar installer\RelojChecador.iss con Inno Setup (ISCC.exe)." -ForegroundColor Cyan
