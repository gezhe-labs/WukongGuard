#if RUNTIME_PROBE
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using b1;
using CSharpModBase;
using CSharpModBase.Input;
using UnrealEngine.Engine;
using UnrealEngine.Runtime;

namespace WukongStateProbe
{
    // Experimental: derived from upstream CSharpModExample/MyUtils.cs.
    // Every game-facing symbol and its behavior require validation on the installed build.
    internal static class RuntimeProbe
    {
        internal static void Register()
        {
            Utils.RegisterKeyBind(ModifierKeys.Control, Key.ENTER, Snapshot);
            Utils.RegisterKeyBind(ModifierKeys.Control, Key.F8, ShowUiProbe);
            Utils.RegisterKeyBind(ModifierKeys.Control, Key.F9, DumpQuestStages);
            Utils.RegisterKeyBind(ModifierKeys.Control, Key.F10, DumpApiCandidates);
            TraceLog.Write("[WukongGuard] probe ready; press Ctrl+Enter in game for a snapshot");
            TraceLog.Write("[WukongGuard] UI probe ready; press Ctrl+F8 in game");
            TraceLog.Write("[WukongGuard] quest dump ready; press Ctrl+F9 in game");
            RuntimeMonitor.Start();
        }

        private static void DumpApiCandidates()
        {
            try
            {
                foreach (Type type in new[] { typeof(BGW_GameDataMgr), typeof(BGWGameInstanceCS),
                    typeof(BPC_PlayerRoleData), typeof(IBPC_PlayerRoleData), typeof(BGP_PlayerControllerB1) })
                {
                    var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => m.Name.IndexOf("Role", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("Chapter", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(m => m.MemberType + " " + m).Distinct().OrderBy(x => x);
                    TraceLog.Write("[WukongGuard] API " + type.FullName + " => " + string.Join(" | ", members));
                }
                var world = GCHelper.FindRef(FGlobals.GWorld)?.Managed as UWorld;
                var mgr = world == null ? null : BGW_GameDataMgr.Get(world);
                var collection = mgr == null ? null : mgr.GetType()
                    .GetProperty("DataCollection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(mgr);
                TraceLog.Write("[WukongGuard] DataCollection=" + (collection == null ? "null" : collection.GetType().FullName + " " + collection));
                if (collection is System.Collections.IDictionary dictionary)
                {
                    foreach (System.Collections.DictionaryEntry item in dictionary)
                        TraceLog.Write("[WukongGuard] data entry=" + item.Key + " type=" + item.Value?.GetType().FullName);
                }
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] API dump error: " + ex);
            }
        }

        internal static void Unregister()
        {
            RuntimeMonitor.Stop();
        }

        private static void DumpQuestStages()
        {
            try
            {
                UObjectRef worldRef = GCHelper.FindRef(FGlobals.GWorld);
                UWorld world = worldRef?.Managed as UWorld;
                var controller = world == null ? null : UGSE_EngineFuncLib.GetFirstLocalPlayerController(world);
                var roleData = controller == null ? null : controller.GetType()
                    .GetMethod("GetReadOnlyDataTodoRemove", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(controller, null) as IBPC_PlayerRoleData;
                var stages = roleData?.RoleData?.RoleCs?.Task?.QuestList;
                if (stages == null)
                {
                    TraceLog.Write("[WukongGuard] quest dump unavailable: role data not loaded");
                    return;
                }
                var text = new StringBuilder();
                foreach (var stage in stages)
                {
                    if (text.Length > 0) text.Append(',');
                    text.Append(stage.Id).Append(':').Append(stage.Stage);
                }
                TraceLog.Write("[WukongGuard] quests=" + text);
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] quest dump error: " + ex);
            }
        }

        private static void ShowUiProbe()
        {
            try
            {
                UObjectRef worldRef = GCHelper.FindRef(FGlobals.GWorld);
                UWorld world = worldRef?.Managed as UWorld;
                if (world == null)
                {
                    TraceLog.Write("[WukongGuard] UI probe skipped: world=null");
                    return;
                }
                USystemLibrary.PrintString(world, "[WukongGuard] UI probe", true, false,
                    new FLinearColor(1f, 0.85f, 0.2f, 1f), 15f, default(FName));
                TraceLog.Write("[WukongGuard] UI probe invoked");
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] UI probe error: " + ex);
            }
        }

        private static void Snapshot()
        {
            try
            {
                UObjectRef worldRef = GCHelper.FindRef(FGlobals.GWorld);
                UWorld world = worldRef?.Managed as UWorld;
                if (world == null)
                {
                    TraceLog.Write("[WukongGuard] world=null (load screen or API mismatch)");
                    return;
                }
                TraceLog.Write("[WukongGuard] world=" + world + " type=" + world.GetType().FullName);

                var controller = UGSE_EngineFuncLib.GetFirstLocalPlayerController(world);
                if (controller == null)
                {
                    TraceLog.Write("[WukongGuard] controller=null");
                    return;
                }
                TraceLog.Write("[WukongGuard] controller=" + controller + " type=" + controller.GetType().FullName);

                try
                {
                    var gameInstance = BGWGameInstanceCS.Get(world);
                    TraceLog.Write(gameInstance == null
                        ? "[WukongGuard] gameInstance=null"
                        : "[WukongGuard] gameInstance=" + gameInstance + " type=" + gameInstance.GetType().FullName);
                }
                catch (Exception ex)
                {
                    TraceLog.Write("[WukongGuard] gameInstance error: " + ex);
                }

                try
                {
                    var dataManager = BGW_GameDataMgr.Get(world);
                    TraceLog.Write("[WukongGuard] dataManager=" + (dataManager == null ? "null" : dataManager.GetType().FullName));
                    var roleData = dataManager?.GetGameInstanceReadonlyData<IBPC_PlayerRoleData, BPC_PlayerRoleData>();
                    if (roleData == null)
                    {
                        var controllerMethod = controller.GetType().GetMethod("GetReadOnlyDataTodoRemove",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        roleData = controllerMethod?.Invoke(controller, null) as IBPC_PlayerRoleData;
                        TraceLog.Write("[WukongGuard] controller roleData=" + (roleData == null ? "null" : roleData.GetType().FullName));
                    }
                    if (roleData == null)
                    {
                        TraceLog.Write("[WukongGuard] roleData=null");
                    }
                    else
                    {
                        var roleCs = roleData.RoleData?.RoleCs;
                        var chapter = roleCs?.Chapter?.CurChapter;
                        TraceLog.Write("[WukongGuard] chapter=" + (chapter?.ToString() ?? "null")
                            + " mapId=" + roleData.MapId
                            + " areaId=" + roleData.MapAreaId
                            + " ngPlus=" + roleData.GetNewGamePlusCount());
                    }
                }
                catch (Exception ex)
                {
                    TraceLog.Write("[WukongGuard] roleData error: " + ex);
                }

                var pawn = controller.GetControlledPawn();
                if (pawn == null)
                {
                    TraceLog.Write("[WukongGuard] pawn=null");
                    return;
                }

                var location = pawn.GetActorLocation();
                TraceLog.Write($"[WukongGuard] world={world} controller={controller} pawn={pawn} location={location}");
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] probe error: " + ex);
            }
        }
    }
}
#endif
