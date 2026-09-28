param(
    [Parameter(Mandatory = $true)][string]$Before,
    [Parameter(Mandatory = $true)][string]$After,
    [string]$GameDll = ''
)

# Calibration only. Reads two save files and never writes either one.
# The XOR key below was verified against ArchiveFile and BGW_GameArchiveMgr
# from the locally tested GameDll; a future game build may change it.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $GameDll) { $GameDll = Join-Path $repo 'WukongStateProbe\vendor-stage\game-dll' }
Add-Type -Path (Join-Path $GameDll 'Google.Protobuf.dll')
Add-Type -Path (Join-Path $GameDll 'Protobuf.RunTime.dll')

function Read-Archive([string]$Path) {
    $file = [ArchiveB1.ArchiveFile]::Parser.ParseFrom(
        [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path))
    $data = $file.GameArchivesDataBytes.ToByteArray()
    if ($file.ArchiveInfo.EnableEncrypt) {
        $key = [BitConverter]::GetBytes([long]4024806562674138235)
        for ($index = 0; $index -lt $data.Length; $index++) {
            $data[$index] = [byte]($data[$index] -bxor $key[$index % $key.Length])
        }
    }
    $result = [ArchiveB1.FUStBEDArchivesData]::Parser.ParseFrom($data)
    if (-not $result.RoleData -or -not $result.RoleData.RoleCs) {
        throw "Archive has no role data: $Path"
    }
    return $result
}

function Show-MapDiff([string]$Label, [hashtable]$BeforeMap, [hashtable]$AfterMap) {
    $changes = @(($BeforeMap.Keys + $AfterMap.Keys | Sort-Object -Unique) |
        Where-Object { $BeforeMap[$_] -ne $AfterMap[$_] } |
        ForEach-Object { "${_}:$($BeforeMap[$_])->$($AfterMap[$_])" })
    Write-Output "$Label before=$($BeforeMap.Count) after=$($AfterMap.Count) changes=$($changes.Count)"
    $changes | Select-Object -First 80
}

$a = Read-Archive $Before
$b = Read-Archive $After
$aQuests = @{}; $bQuests = @{}
foreach ($quest in $a.RoleData.RoleCs.Task.QuestList) { $aQuests[[string]$quest.Id] = [string]$quest.Stage }
foreach ($quest in $b.RoleData.RoleCs.Task.QuestList) { $bQuests[[string]$quest.Id] = [string]$quest.Stage }
Show-MapDiff 'QuestStages' $aQuests $bQuests

$aItems = @{}; $bItems = @{}
foreach ($item in $a.RoleData.RoleCs.Bag.ItemList) {
    $aItems[[string]$item.ItemId] = "$( $item.Num )/$( $item.StoreNum )"
}
foreach ($item in $b.RoleData.RoleCs.Bag.ItemList) {
    $bItems[[string]$item.ItemId] = "$( $item.Num )/$( $item.StoreNum )"
}
Show-MapDiff 'ItemQuantities' $aItems $bItems

$aFlags = @{}; $bFlags = @{}
foreach ($id in $a.RoleData.RoleCs.Interaction.InteractionFuncList) { $aFlags[[string]$id] = 'present' }
foreach ($id in $b.RoleData.RoleCs.Interaction.InteractionFuncList) { $bFlags[[string]$id] = 'present' }
Show-MapDiff 'RoleInteractionIds' $aFlags $bFlags

$aRecords = @{}; $bRecords = @{}
foreach ($record in $a.TaskArchiveData.InteractionRecordList) {
    $aRecords[[string]$record.InteractGroupId] = [string]$record.InteractStep
}
foreach ($record in $b.TaskArchiveData.InteractionRecordList) {
    $bRecords[[string]$record.InteractGroupId] = [string]$record.InteractStep
}
Show-MapDiff 'WorldInteractionRecords' $aRecords $bRecords

$aGraphs = @{}; $bGraphs = @{}
foreach ($graph in $a.TaskArchiveData.TaskGraphDataInfoList) { $aGraphs[[string]$graph.TaskLineId] = $graph }
foreach ($graph in $b.TaskArchiveData.TaskGraphDataInfoList) { $bGraphs[[string]$graph.TaskLineId] = $graph }
$graphChanges = @(($aGraphs.Keys + $bGraphs.Keys | Sort-Object -Unique) |
    Where-Object { -not $aGraphs[$_].Equals($bGraphs[$_]) })
Write-Output "ChangedTaskGraphIds=$($graphChanges -join ',')"

$aMachines = @{}; $bMachines = @{}
foreach ($machine in $a.StateMachineArchiveData.PsmArchiveData) { $aMachines[$machine.PsmId] = $machine }
foreach ($machine in $b.StateMachineArchiveData.PsmArchiveData) { $bMachines[$machine.PsmId] = $machine }
$machineChanges = @(($aMachines.Keys + $bMachines.Keys | Sort-Object -Unique) |
    Where-Object { -not $aMachines[$_].Equals($bMachines[$_]) })
Write-Output "ChangedPsmIds=$($machineChanges -join ',')"
foreach ($id in $machineChanges) {
    $beforeNodes = @{}; $afterNodes = @{}
    if ($aMachines[$id]) {
        foreach ($node in $aMachines[$id].NodeData) {
            $beforeNodes[[string]$node.UniqueId] = [string]$node.ActivationState
        }
    }
    if ($bMachines[$id]) {
        foreach ($node in $bMachines[$id].NodeData) {
            $afterNodes[[string]$node.UniqueId] = [string]$node.ActivationState
        }
    }
    Show-MapDiff "PsmNodes $id" $beforeNodes $afterNodes
}
