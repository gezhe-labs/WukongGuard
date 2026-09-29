param(
    [string]$GameRoot = '',
    [switch]$PurgeSettings
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'find-game.ps1')
$game = Find-WukongGameRoot $GameRoot
if (Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue) {
    throw 'Close the game before uninstalling WukongGuard.'
}
$modDir = Join-Path $game 'b1\Binaries\Win64\CSharpLoader\Mods\WukongGuard'
if (-not (Test-Path -LiteralPath $modDir)) {
    Write-Output 'WukongGuard is not installed.'
    return
}
$resolvedMod = (Resolve-Path -LiteralPath $modDir).Path.TrimEnd('\')
$manifest = Join-Path $resolvedMod '.release-files.txt'
if (-not (Test-Path -LiteralPath $manifest)) {
    throw 'Release file manifest is missing. No files were removed.'
}
$ownedDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in (Get-Content -LiteralPath $manifest)) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $file = [IO.Path]::GetFullPath((Join-Path $resolvedMod $relative))
    if (-not $file.StartsWith($resolvedMod + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe manifest path: $relative"
    }
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    $parent = Split-Path $file
    while ($parent.StartsWith($resolvedMod + '\', [StringComparison]::OrdinalIgnoreCase)) {
        [void]$ownedDirectories.Add($parent)
        $parent = Split-Path $parent
    }
}
Remove-Item -LiteralPath $manifest
if ($PurgeSettings) {
    foreach ($name in @('rules.json', 'experience.json')) {
        $file = Join-Path $resolvedMod $name
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    }
    foreach ($name in @('settings.json', 'history.jsonl', 'runtime-status.txt')) {
        $localFile = Join-Path $env:LOCALAPPDATA ('WukongGuard\' + $name)
        if (Test-Path -LiteralPath $localFile -PathType Leaf) {
            Remove-Item -LiteralPath $localFile
        }
    }
}
foreach ($directory in ($ownedDirectories | Sort-Object -Property Length -Descending)) {
    if ((Test-Path -LiteralPath $directory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $directory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $directory
    }
}
if (@(Get-ChildItem -LiteralPath $resolvedMod -Force).Count -eq 0) {
    Remove-Item -LiteralPath $resolvedMod
}
Write-Output 'WukongGuard program files removed. Backups under CSharpLoader\Backups were kept.'
