function Find-WukongGameRoot([string]$ExplicitRoot) {
    if ($ExplicitRoot) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitRoot -ErrorAction Stop).Path
        if (-not (Test-Path -LiteralPath (Join-Path $resolved 'b1\Binaries\Win64\b1-Win64-Shipping.exe'))) {
            throw "Black Myth: Wukong executable not found under $resolved"
        }
        return $resolved
    }

    $steamRoots = [System.Collections.Generic.List[string]]::new()
    foreach ($key in @('HKCU:\Software\Valve\Steam',
            'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
        if ($entry.SteamPath) { $steamRoots.Add([string]$entry.SteamPath) }
        if ($entry.InstallPath) { $steamRoots.Add([string]$entry.InstallPath) }
    }
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem)) {
        foreach ($suffix in @('Steam', 'SteamLibrary', 'Program Files (x86)\Steam',
                'Program Files\Steam')) {
            $steamRoots.Add((Join-Path $drive.Root $suffix))
        }
    }

    $libraries = [System.Collections.Generic.List[string]]::new()
    foreach ($root in ($steamRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $libraries.Add($root)
        $foldersFile = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $foldersFile)) { continue }
        foreach ($line in (Get-Content -LiteralPath $foldersFile)) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraries.Add(($Matches[1] -replace '\\\\', '\'))
            }
        }
    }

    $matches = @($libraries | Select-Object -Unique | ForEach-Object {
        $candidate = Join-Path $_ 'steamapps\common\BlackMythWukong'
        if (Test-Path -LiteralPath (Join-Path $candidate 'b1\Binaries\Win64\b1-Win64-Shipping.exe')) {
            (Resolve-Path -LiteralPath $candidate).Path
        }
    } | Select-Object -Unique)
    if ($matches.Count -eq 1) { return $matches[0] }
    if ($matches.Count -gt 1) { throw 'Multiple installations found. Pass -GameRoot explicitly.' }
    throw 'Black Myth: Wukong not found. Pass -GameRoot with the installation folder.'
}
