param([string]$Executable = '')

$ErrorActionPreference = 'Stop'
if (Get-Process -Name 'b1-Win64-Shipping' -ErrorAction SilentlyContinue) { throw 'Close the real game before the smoke test.' }
if (-not $Executable) { $Executable = Join-Path $PSScriptRoot 'dist\后悔药-0.5.0-rc5.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$stage = Join-Path $PSScriptRoot 'dist-stage'
$fixture = Join-Path $stage ('smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bin = Join-Path $fixture 'b1\Binaries\Win64'
$loader = Join-Path $bin 'CSharpLoader'
New-Item -ItemType Directory -Path $loader -Force | Out-Null
Set-Content -LiteralPath (Join-Path $bin 'b1-Win64-Shipping.exe') -Value 'fixture'
Set-Content -LiteralPath (Join-Path $bin 'version.dll') -Value 'fixture'
Set-Content -LiteralPath (Join-Path $loader 'CSharpModBase.dll') -Value 'fixture'
Set-Content -LiteralPath (Join-Path $loader 'b1cs.ini') `
    -Value "[Settings]`r`nDevelop=0`r`nConsole=1`r`nEnableJit=1`r`n"

$verify = Start-Process -FilePath $Executable -ArgumentList '--verify' `
    -PassThru -Wait -WindowStyle Hidden
if ($verify.ExitCode -ne 0) { throw 'Payload integrity check failed.' }
$prepare = Start-Process -FilePath $Executable -ArgumentList @('--smoke-install','--game-root',('"' + $fixture + '"')) `
    -PassThru -Wait -WindowStyle Hidden
if ($prepare.ExitCode -ne 0) { throw 'Fixture preparation failed.' }
$ini = @(Get-Content -LiteralPath (Join-Path $loader 'b1cs.ini'))
if (@($ini | Where-Object { $_ -eq 'Console=0' }).Count -ne 1 `
    -or @($ini | Where-Object { $_ -eq 'EnableJit=0' }).Count -ne 1 `
    -or @($ini | Where-Object { $_ -match '^(Console|EnableJit)=' }).Count -ne 2) {
    throw 'Loader settings were not replaced cleanly.'
}
if (@(Get-ChildItem -LiteralPath $loader -Filter 'b1cs.ini.before-regretpill-*.bak').Count -ne 1) {
    throw 'Previous loader settings were not backed up.'
}
$mod = Join-Path $loader 'Mods\WukongGuard'
foreach ($file in @('WukongGuard.mod-disabled','rules.json','Overlay\WukongGuard.Overlay.exe',
        '.product-version.txt','.package-sha256.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $mod $file))) { throw "Missing $file" }
}
if ((Get-Content -LiteralPath (Join-Path $mod '.product-version.txt') -Raw).Trim() -ne '0.5.0-rc5') {
    throw 'Incorrect product version marker.'
}
if (Test-Path -LiteralPath (Join-Path $mod 'WukongGuard.dll')) { throw 'Mod was left active.' }
$session = Start-Process -FilePath $Executable -ArgumentList @('--smoke-session','--game-root',('"' + $fixture + '"')) `
    -PassThru -Wait -WindowStyle Hidden
if ($session.ExitCode -ne 0) { throw 'Session arm/heartbeat/disarm failed.' }
$preview = Join-Path $fixture 'ui-preview'
$ui = Start-Process -FilePath $Executable -ArgumentList @('--smoke-ui','--game-root',('"' + $fixture + '"'),
    '--output',('"' + $preview + '"')) -PassThru -Wait -WindowStyle Hidden
if ($ui.ExitCode -ne 0) { throw "Launcher UI checks failed. See $preview" }
$uninstall = Join-Path $PSScriptRoot '..\WukongGuard\release\uninstall.ps1'
& $uninstall -GameRoot $fixture | Out-Null
foreach ($file in @('WukongGuard.mod-disabled','.product-version.txt','.package-sha256.txt')) {
    if (Test-Path -LiteralPath (Join-Path $mod $file)) { throw "Uninstall left $file" }
}
Write-Output "PASS: payload, backup, quiet loader, trainer setting, session lifetime, launcher UI, uninstall"
Write-Output "Fixture: $fixture"
Write-Output "UI checks: $(Join-Path $preview 'ui-smoke.txt')"
