param(
    [string]$GameRoot = '',
    [string]$GameDll = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'find-game.ps1')
$GameRoot = Find-WukongGameRoot $GameRoot
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GameDll) { $GameDll = Join-Path $repo 'WukongStateProbe\vendor-stage\game-dll' }
$gameBin = Join-Path $GameRoot 'b1\Binaries\Win64'
$localDotnet = Join-Path $repo 'WukongStateProbe\.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = Split-Path $localDotnet
    $env:DOTNET_CLI_HOME = Join-Path $repo 'WukongStateProbe\.tools\home'
    $env:NUGET_PACKAGES = Join-Path $repo 'WukongStateProbe\.tools\nuget'
} else {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:NuGetAudit = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$name = 'WukongGuard-0.4.0-rc11-' + $stamp
$publish = Join-Path $repo ('WukongGuard\dist-stage\' + $name + '\Overlay')
$package = Join-Path $repo ('WukongGuard\dist\' + $name)
$zip = $package + '.zip'
New-Item -ItemType Directory -Path $publish, $package -Force | Out-Null

function Copy-DirectoryContents([string]$source, [string]$destination) {
    if (-not (Test-Path -LiteralPath $source)) { throw "Runtime component missing: $source" }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
}

& $dotnet restore (Join-Path $repo 'WukongGuard\WukongGuard.csproj') `
    "-p:GameBin=$gameBin" "-p:GameDll=$GameDll" --ignore-failed-sources --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mod restore failed' }
& $dotnet build (Join-Path $repo 'WukongGuard\WukongGuard.csproj') -c Release --no-restore `
    "-p:GameBin=$gameBin" "-p:GameDll=$GameDll" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed' }
& $dotnet publish (Join-Path $repo 'WukongGuard.Overlay\WukongGuard.Overlay.csproj') `
    -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false `
    -p:PublishTrimmed=false --ignore-failed-sources -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Overlay publish failed' }

Copy-Item -LiteralPath (Join-Path $repo 'WukongGuard\bin\Release\net472\WukongGuard.dll') `
    -Destination (Join-Path $package 'WukongGuard.dll')
foreach ($name in @('rules.json', 'experience.json')) {
    Copy-Item -LiteralPath (Join-Path $repo ('WukongGuard\' + $name)) -Destination (Join-Path $package $name)
}
New-Item -ItemType Directory -Path (Join-Path $package 'data') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'WukongGuard\data\ch1-ch6-missables.json') `
    -Destination (Join-Path $package 'data\ch1-ch6-missables.json')
Copy-Item -LiteralPath (Join-Path $repo 'WukongGuard\data\hidden-areas.json') `
    -Destination (Join-Path $package 'data\hidden-areas.json')
New-Item -ItemType Directory -Path (Join-Path $package 'Overlay'), (Join-Path $package 'release') -Force | Out-Null
foreach ($name in @('WukongGuard.Overlay.exe', 'WukongGuard.Overlay.dll',
        'WukongGuard.Overlay.deps.json', 'WukongGuard.Overlay.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $publish $name) -Destination (Join-Path (Join-Path $package 'Overlay') $name)
}
$sdkRoot = Split-Path $dotnet
$desktopVersions = @(Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'shared\Microsoft.WindowsDesktop.App') `
    -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '8.*' } |
    Sort-Object -Property Name -Descending)
if ($desktopVersions.Count -eq 0) { throw '.NET 8 Windows Desktop runtime missing from the build SDK.' }
$runtimeVersion = $desktopVersions[0].Name
$runtimeTarget = Join-Path $package 'Overlay\Runtime'
New-Item -ItemType Directory -Path $runtimeTarget -Force | Out-Null
foreach ($name in @('dotnet.exe', 'LICENSE.txt', 'ThirdPartyNotices.txt')) {
    Copy-Item -LiteralPath (Join-Path $sdkRoot $name) -Destination (Join-Path $runtimeTarget $name)
}
Copy-DirectoryContents (Join-Path $sdkRoot ('host\fxr\' + $runtimeVersion)) `
    (Join-Path $runtimeTarget ('host\fxr\' + $runtimeVersion))
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    Copy-DirectoryContents (Join-Path $sdkRoot ('shared\' + $framework + '\' + $runtimeVersion)) `
        (Join-Path $runtimeTarget ('shared\' + $framework + '\' + $runtimeVersion))
}
foreach ($name in @('find-game.ps1', 'install.ps1', 'start-session.ps1', 'uninstall.ps1',
        'export-diagnostics.ps1', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path (Join-Path $package 'release') $name)
}
$hashes = Get-ChildItem -LiteralPath $package -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length + 1)
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $relative
}
$hashes | Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding UTF8
Compress-Archive -LiteralPath $package -DestinationPath $zip
Write-Output "Package: $zip"
Write-Output "Bundled .NET Desktop Runtime: $runtimeVersion (x64)."
