param(
    [string]$GameRoot = '',
    [switch]$UpdateRules
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'WukongGuard\release\find-game.ps1')
$GameRoot = Find-WukongGameRoot $GameRoot
$gameBin = Join-Path $GameRoot 'b1\Binaries\Win64'
$loader = Join-Path $gameBin 'CSharpLoader\CSharpModBase.dll'
if (-not (Test-Path -LiteralPath $loader)) { throw "B1CSharpLoader not found: $loader" }
$source = Join-Path $repo 'WukongGuard\bin\Release\net472\WukongGuard.dll'
if (-not (Test-Path -LiteralPath $source)) { throw 'Build the Mod first.' }
$modDir = Join-Path $gameBin 'CSharpLoader\Mods\WukongGuard'
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $modDir 'WukongGuard.dll') -Force
$rules = Join-Path $modDir 'rules.json'
$sourceRules = Join-Path $repo 'WukongGuard\rules.json'
if (-not (Test-Path -LiteralPath $rules)) {
    Copy-Item -LiteralPath $sourceRules -Destination $rules
} elseif ($UpdateRules -and (Get-FileHash -LiteralPath $rules).Hash -ne (Get-FileHash -LiteralPath $sourceRules).Hash) {
    $backup = Join-Path $modDir ('rules.json.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Copy-Item -LiteralPath $rules -Destination $backup
    Copy-Item -LiteralPath $sourceRules -Destination $rules -Force
    Write-Output "Previous rules backed up: $backup"
}
$experience = Join-Path $modDir 'experience.json'
if (-not (Test-Path -LiteralPath $experience)) {
    Copy-Item -LiteralPath (Join-Path $repo 'WukongGuard\experience.json') -Destination $experience
}
Write-Output "Installed: $modDir"
Write-Output 'Existing rules.json and experience.json are preserved unless rules are updated explicitly. Restart the game, or use Ctrl+F5 with Develop=1.'
