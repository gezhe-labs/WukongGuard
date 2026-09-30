param(
    [string]$OutputDirectory = (Get-Location).Path,
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Stop'
$log = if ($LogPath) { $LogPath }
    else { Join-Path $env:LOCALAPPDATA 'WukongGuard\guard.log' }
if (-not (Test-Path -LiteralPath $log)) { throw "Log not found: $log" }
$target = (Resolve-Path -LiteralPath $OutputDirectory -ErrorAction Stop).Path
$output = Join-Path $target ('WukongGuard-diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
$userProfilePattern = [regex]::Escape($env:USERPROFILE)
$lines = Get-Content -LiteralPath $log -Tail 500 | Where-Object {
    $_ -notmatch '\] (quest snapshot|item snapshot|interaction snapshot|world interaction snapshot|psm candidate snapshot) '
} | ForEach-Object {
    $line = $_ -replace 'xyz=[^ ]+', 'xyz=<redacted>'
    $line = [regex]::Replace($line, $userProfilePattern, '%USERPROFILE%', 'IgnoreCase')
    $line
}
$overlayLines = foreach ($name in @('overlay.log', 'overlay.log.previous')) {
    $overlayPath = Join-Path (Split-Path -Parent $log) $name
    if (Test-Path -LiteralPath $overlayPath) {
        "--- $name ---"
        Get-Content -LiteralPath $overlayPath -Tail 100
    }
}
@(
    'WukongGuard local diagnostics (review before sharing)',
    'Saved-game files are not included.',
    ''
) + $lines + @($overlayLines) | Set-Content -LiteralPath $output -Encoding UTF8
Write-Output "Diagnostics saved: $output"
