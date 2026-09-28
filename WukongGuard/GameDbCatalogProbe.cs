using System;
using System.Linq;
using System.Reflection;
using b1;
using b1.Localization;

namespace WukongGuard
{
    internal static class GameDbCatalogProbe
    {
        private static bool itemLogged;
        private static bool accessoryLogged;
        private static bool areaLogged;
        private static bool bossLogged;
        private static bool shrineLogged;
        private static bool taskLogged;

        // Development-only lookup of game data. No item or area IDs are assumed.
        internal static void Sample(int? mapId, int? areaId)
        {
            if (!itemLogged)
            {
                try
                {
                    var items = GameDBRuntime.GetTBItemDesc()?.List;
                    if (items != null && items.Count > 0)
                    {
                        itemLogged = true;
                        var names = items.Select(item => item.Id + ":"
                            + GSLocalization.GSLocalizational(item.Name)).ToArray();
                        var matches = names.Where(name => name.Contains("吉祥灯")
                            || name.IndexOf("Auspicious Lantern", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToArray();
                        TraceLog.Write("[WukongGuard] item catalog count=" + items.Count
                            + " lantern=" + string.Join(",", matches)
                            + " examples=" + string.Join(",", names.Take(8)));
                    }
                }
                catch (Exception ex)
                {
                    itemLogged = true;
                    TraceLog.Write("[WukongGuard] item catalog lookup failed: " + ex);
                }
            }
            if (!accessoryLogged)
            {
                try
                {
                    var equips = GameDBRuntime.GetTBEquipDesc()?.List;
                    if (equips != null && equips.Count > 0)
                    {
                        accessoryLogged = true;
                        var names = equips.Where(equip => equip.EquipPosition.ToString() == "Accessory")
                            .Select(equip => equip.Id + ":"
                                + GSLocalization.GSLocalizational(equip.EquipName)).ToArray();
                        var matches = names.Where(name => name.Contains("吉祥灯")
                            || name.IndexOf("Auspicious Lantern", StringComparison.OrdinalIgnoreCase) >= 0)
                            .ToArray();
                        TraceLog.Write("[WukongGuard] accessory catalog count=" + names.Length
                            + " lantern=" + string.Join(",", matches)
                            + " examples=" + string.Join(",", names.Take(8)));
                    }
                }
                catch (Exception ex)
                {
                    accessoryLogged = true;
                    TraceLog.Write("[WukongGuard] accessory catalog lookup failed: " + ex);
                }
            }
            if (!areaLogged)
            {
                try
                {
                    var areas = GameDBRuntime.GetTBMapAreaConfigDesc()?.List;
                    if (areas != null && areas.Count > 0)
                    {
                        areaLogged = true;
                        var named = areas.Select(area => new {
                            Area = area,
                            Name = GSLocalization.GSLocalizational(area.AreaName)
                        }).ToArray();
                        var matches = named.Where(row => row.Name.Contains("浮屠")
                            || row.Name.IndexOf("Pagoda", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(row => row.Area.Id + "/" + row.Area.LevelId + "/" + row.Area.AreaId
                                + ":" + row.Name).Take(30).ToArray();
                        var current = named.Where(row => row.Area.LevelId == mapId && row.Area.AreaId == areaId)
                            .Select(row => row.Area.Id + ":" + row.Name).Take(5).ToArray();
                        TraceLog.Write("[WukongGuard] area catalog count=" + areas.Count
                            + " pagoda=" + string.Join(",", matches)
                            + " current=" + string.Join(",", current));
                        var thirdChapter = named.Where(row => row.Area.LevelId == 30)
                            .Select(row => row.Area.Id + "/" + row.Area.AreaId + ":" + row.Name)
                            .ToArray();
                        TraceLog.Write("[WukongGuard] chapter3 map areas=" + string.Join(",", thirdChapter));
                    }
                }
                catch (Exception ex)
                {
                    areaLogged = true;
                    TraceLog.Write("[WukongGuard] area catalog lookup failed: " + ex);
                }
            }
            if (!bossLogged)
            {
                try
                {
                    var bosses = GameDBRuntime.GetTBSceneMonsterNameplateDesc()?.List;
                    if (bosses != null && bosses.Count > 0)
                    {
                        bossLogged = true;
                        var matches = bosses.Select(boss => new {
                            Boss = boss,
                            Name = GSLocalization.GSLocalizational(boss.Name)
                        }).Where(row => row.Name.Contains("妙音")
                            || row.Name.IndexOf("Wise-Voice", StringComparison.OrdinalIgnoreCase) >= 0)
                            .Select(row => row.Boss.Id + "/" + row.Boss.LevelId + ":" + row.Name)
                            .ToArray();
                        TraceLog.Write("[WukongGuard] boss nameplate count=" + bosses.Count
                            + " wise_voice=" + string.Join(",", matches));
                    }
                }
                catch (Exception ex)
                {
                    bossLogged = true;
                    TraceLog.Write("[WukongGuard] boss nameplate lookup failed: " + ex);
                }
            }
            if (!shrineLogged)
            {
                try
                {
                    var shrines = GameDBRuntime.GetTBFUStRebirthPointDesc();
                    if (shrines != null && shrines.Count > 0)
                    {
                        shrineLogged = true;
                        var chapterThree = shrines.Values.Where(shrine => shrine.GroupMapID == 30)
                            .Select(shrine => shrine.ID + "/" + shrine.GroupMapID + "/"
                                + shrine.GroupAreaID + ":"
                                + GSLocalization.GSLocalizational(shrine.Name)).ToArray();
                        TraceLog.Write("[WukongGuard] shrine catalog count=" + shrines.Count
                            + " chapter3=" + string.Join(",", chapterThree));
                        foreach (var id in new[] { 3005, 3007, 3024, 3026 })
                        {
                            if (!shrines.TryGetValue(id, out var shrine)) continue;
                            var fields = shrine.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                .Where(property => property.GetIndexParameters().Length == 0)
                                .Select(property => property.Name + "=" + property.GetValue(shrine))
                                .ToArray();
                            TraceLog.Write("[WukongGuard] shrine fields " + id + " "
                                + string.Join(",", fields));
                        }
                    }
                }
                catch (Exception ex)
                {
                    shrineLogged = true;
                    TraceLog.Write("[WukongGuard] shrine catalog lookup failed: " + ex);
                }
            }
            if (!taskLogged)
            {
                try
                {
                    var stages = GameDBRuntime.GetTBFUStTaskStageDesc();
                    var lines = GameDBRuntime.GetTBFUStTaskLineDesc();
                    if (stages != null && stages.Count > 0 && lines != null && lines.Count > 0)
                    {
                        taskLogged = true;
                        var matches = stages.Values.Where(stage => stage.Describe != null
                            && (stage.Describe.Contains("马天霸") || stage.Describe.Contains("马哥")
                                || stage.Describe.IndexOf("horse", StringComparison.OrdinalIgnoreCase) >= 0))
                            .Select(stage => stage.ID + "/" + stage.BelongsToLineID
                                + ":" + stage.Describe).Take(40).ToArray();
                        var paths = lines.Values.Where(line => line.TaskGraphAssetPath != null
                            && (line.TaskGraphAssetPath.IndexOf("horse", StringComparison.OrdinalIgnoreCase) >= 0
                                || line.TaskGraphAssetPath.Contains("马")))
                            .Select(line => line.ID + ":" + line.TaskGraphAssetPath).Take(30).ToArray();
                        TraceLog.Write("[WukongGuard] task catalog stages=" + stages.Count
                            + " horse=" + string.Join(",", matches)
                            + " paths=" + string.Join(",", paths));
                        // The task names are often stored as asset paths or opaque IDs.
                        // Log only chapter 2/3 candidates once so the live table can be mapped
                        // without guessing that a localized NPC name exists in Describe.
                        var candidateStages = stages.Values
                            .Where(stage => stage.ID.ToString().StartsWith("2")
                                || stage.ID.ToString().StartsWith("3")
                                || stage.BelongsToLineID.ToString().StartsWith("2")
                                || stage.BelongsToLineID.ToString().StartsWith("3"))
                            .Select(stage => stage.ID + "/" + stage.BelongsToLineID
                                + ":" + stage.Describe).ToArray();
                        var candidateLines = lines.Values
                            .Select(line => line.ID + ":" + line.TaskGraphAssetPath).ToArray();
                        foreach (var chunk in candidateStages.Select((entry, index) => new { entry, index })
                            .GroupBy(row => row.index / 30))
                            TraceLog.Write("[WukongGuard] task stage candidates " + chunk.Key
                                + " " + string.Join(",", chunk.Select(row => row.entry)));
                        foreach (var chunk in candidateLines.Select((entry, index) => new { entry, index })
                            .GroupBy(row => row.index / 30))
                            TraceLog.Write("[WukongGuard] task line candidates " + chunk.Key
                                + " " + string.Join(",", chunk.Select(row => row.entry)));
                    }
                }
                catch (Exception ex)
                {
                    taskLogged = true;
                    TraceLog.Write("[WukongGuard] task catalog lookup failed: " + ex);
                }
            }
        }
    }
}
