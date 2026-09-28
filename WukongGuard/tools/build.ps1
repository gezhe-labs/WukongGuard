param(
    [string]$GameRoot = '',
    [string]$GameDll = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'WukongGuard\release\find-game.ps1')
$GameRoot = Find-WukongGameRoot $GameRoot
if (-not $GameDll) { $GameDll = Join-Path $repo 'WukongStateProbe\vendor-stage\game-dll' }
$gameBin = Join-Path $GameRoot 'b1\Binaries\Win64'
$overlayDll = Join-Path $repo 'WukongGuard.Overlay\bin\Release\net8.0-windows\WukongGuard.Overlay.dll'
if (Test-Path -LiteralPath $overlayDll) {
    try {
        $handle = [IO.File]::Open($overlayDll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $handle.Dispose()
    } catch { throw 'WukongGuard Overlay is running. Exit it from the system tray before building.' }
}
$localDotnet = Join-Path $repo 'WukongStateProbe\.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = Split-Path $localDotnet
    $env:DOTNET_CLI_HOME = Join-Path $repo 'WukongStateProbe\.tools\home'
    $env:NUGET_PACKAGES = Join-Path $repo 'WukongStateProbe\.tools\nuget'
} else {
    $dotnet = 'dotnet'
}
$env:NuGetAudit = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& $dotnet restore (Join-Path $repo 'WukongGuard\WukongGuard.csproj') `
    "-p:GameBin=$gameBin" "-p:GameDll=$GameDll" --ignore-failed-sources --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mod restore failed' }
& $dotnet build (Join-Path $repo 'WukongGuard\WukongGuard.csproj') -c Release --no-restore `
    "-p:GameBin=$gameBin" "-p:GameDll=$GameDll" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed' }

& $dotnet restore (Join-Path $repo 'WukongGuard.Overlay\WukongGuard.Overlay.csproj') --ignore-failed-sources --nologo
if ($LASTEXITCODE -ne 0) { throw 'Overlay restore failed' }
& $dotnet build (Join-Path $repo 'WukongGuard.Overlay\WukongGuard.Overlay.csproj') -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Overlay build failed' }

Write-Output 'WukongGuard Mod and Overlay built.'
