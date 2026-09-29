param(
    [string]$PackageZip = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $PackageZip) {
    $latest = Get-ChildItem -LiteralPath (Join-Path $repo 'WukongGuard\dist') `
        -Filter 'WukongGuard-0.5.0-rc1-*.zip' -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw 'Build WukongGuard/release/build-package.ps1 first.' }
    $PackageZip = $latest.FullName
}
$PackageZip = (Resolve-Path -LiteralPath $PackageZip).Path
$dotnet = Join-Path $repo 'WukongStateProbe\.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_CLI_HOME = Join-Path $repo 'WukongStateProbe\.tools\home'
$env:NUGET_PACKAGES = Join-Path $repo 'WukongStateProbe\.tools\nuget'
$env:NuGetAudit = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$stage = Join-Path $PSScriptRoot 'dist-stage'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $stage, $dist -Force | Out-Null
& $dotnet publish (Join-Path $PSScriptRoot 'WukongGuard.Installer.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
    "-p:PayloadZip=$PackageZip" --ignore-failed-sources -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

$exe = Join-Path $dist '后悔药-0.5.0-rc1.exe'
Copy-Item -LiteralPath (Join-Path $stage 'WukongGuard.Installer.exe') -Destination $exe -Force
Write-Output "Installer: $exe"
Write-Output "SHA256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
