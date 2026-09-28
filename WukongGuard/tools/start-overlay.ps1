$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$dll = Join-Path $repo 'WukongGuard.Overlay\bin\Release\net8.0-windows\WukongGuard.Overlay.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the Overlay first.' }
$localDotnet = Join-Path $repo 'WukongStateProbe\.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = Split-Path $localDotnet
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$process = Start-Process -FilePath $dotnet -ArgumentList @('"' + $dll + '"') -WindowStyle Hidden -PassThru
Write-Output "WukongGuard Overlay launched (PID $($process.Id)). If the game is not running, start it through Steam."
