param([string]$GameRoot = '')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'find-game.ps1')
$game = Find-WukongGameRoot $GameRoot
$mod = Join-Path $game 'b1\Binaries\Win64\CSharpLoader\Mods\WukongGuard'
$source = Join-Path $mod 'WukongGuard.mod-disabled'
$active = Join-Path $mod 'WukongGuard.dll'
if (-not (Test-Path -LiteralPath $source)) { throw 'Install WukongGuard first.' }
if (Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue) {
    throw 'Close the game before starting a WukongGuard session.'
}
$sessionDir = Join-Path $env:LOCALAPPDATA 'WukongGuard'
$sessionFile = Join-Path $sessionDir 'active-session.txt'
New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
Set-Content -LiteralPath $sessionFile -Value 'armed' -NoNewline -Encoding Ascii
Copy-Item -LiteralPath $source -Destination $active -Force
Write-Output 'WukongGuard armed for one game session. Keep this window open and start the game through Steam.'
try {
    $seen = $false
    while ($true) {
        Start-Sleep -Seconds 1
        [IO.File]::SetLastWriteTimeUtc($sessionFile, [DateTime]::UtcNow)
        $running = [bool](Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue)
        if ($running) { $seen = $true }
        elseif ($seen) { break }
    }
}
finally {
    if (-not (Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $active -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $sessionFile -Force -ErrorAction SilentlyContinue
        Write-Output 'Game exited. WukongGuard disabled.'
    }
}
