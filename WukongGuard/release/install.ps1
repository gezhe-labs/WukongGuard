param(
    [string]$GameRoot = '',
    [switch]$ResetSettings,
    [string]$RuntimeRoot = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'find-game.ps1')
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$game = Find-WukongGameRoot $GameRoot
$bin = Join-Path $game 'b1\Binaries\Win64'
$loader = Join-Path $bin 'CSharpLoader\CSharpModBase.dll'
if (-not (Test-Path -LiteralPath $loader)) {
    throw 'B1CSharpLoader is required. Install it first, then rerun this script.'
}
if (Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue) {
    throw 'Close the game before installing WukongGuard.'
}
$bundledRuntime = Join-Path $packageRoot 'Overlay\Runtime\dotnet.exe'
if (-not (Test-Path -LiteralPath $bundledRuntime)) {
    $desktopRuntime = if ($RuntimeRoot) { $RuntimeRoot }
        else { Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App' }
    $runtime8 = @(Get-ChildItem -LiteralPath $desktopRuntime -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '8.*' })
    if ($runtime8.Count -eq 0) {
        throw '.NET 8 Desktop Runtime (x64) is required for the overlay. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and rerun this script.'
    }
}
$modDll = Join-Path $packageRoot 'WukongGuard.dll'
$overlayDir = Join-Path $packageRoot 'Overlay'
if (-not (Test-Path -LiteralPath $modDll) -or
    -not (Test-Path -LiteralPath (Join-Path $overlayDir 'WukongGuard.Overlay.exe'))) {
    throw 'Package is incomplete: WukongGuard.dll or Overlay executable is missing.'
}
$modDir = Join-Path $bin 'CSharpLoader\Mods\WukongGuard'
if (Test-Path -LiteralPath $modDir) {
    $backupRoot = Join-Path $bin 'CSharpLoader\Backups'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ('WukongGuard-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item -LiteralPath $modDir -Destination $backup -Recurse
    Write-Output "Previous installation backed up: $backup"
}
New-Item -ItemType Directory -Path $modDir -Force | Out-Null
Copy-Item -LiteralPath $modDll -Destination (Join-Path $modDir 'WukongGuard.dll') -Force
# A release must activate its shipped rules even when upgrading a development install.
# The directory backup above retains the previous rules for recovery.
Copy-Item -LiteralPath (Join-Path $packageRoot 'rules.json') `
    -Destination (Join-Path $modDir 'rules.json') -Force
$experience = Join-Path $modDir 'experience.json'
if ($ResetSettings -or -not (Test-Path -LiteralPath $experience)) {
    Copy-Item -LiteralPath (Join-Path $packageRoot 'experience.json') -Destination $experience -Force
}
$targetOverlay = Join-Path $modDir 'Overlay'
New-Item -ItemType Directory -Path $targetOverlay -Force | Out-Null
$ownedFiles = [System.Collections.Generic.List[string]]::new()
$ownedFiles.Add('WukongGuard.dll')
foreach ($file in (Get-ChildItem -LiteralPath $overlayDir -File -Recurse)) {
    $relative = $file.FullName.Substring($overlayDir.Length + 1)
    $destination = Join-Path $targetOverlay $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    $ownedFiles.Add(('Overlay\' + $relative))
}
$ownedFiles | Set-Content -LiteralPath (Join-Path $modDir '.release-files.txt') -Encoding UTF8
Write-Output "Installed WukongGuard: $modDir"
Write-Output 'Start the game through Steam. The Mod will launch the overlay automatically.'
