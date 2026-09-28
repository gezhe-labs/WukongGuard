using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using b1;
using UnrealEngine.Engine;
using UnrealEngine.Runtime;
using WukongGuard.Core;

namespace WukongGuard
{
    internal static class GameStateAdapter
    {
        private static MethodInfo roleMethod;
        private static readonly PropertyInfo TaskDataProperty = typeof(BIS_TaskManager).GetProperty(
            "TaskData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo StateMachineDataProperty = typeof(BIS_StateMachineManager).GetProperty(
            "StateMachineData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool taskSourceErrorLogged;
        private static bool psmSourceErrorLogged;
        private static HashSet<string> trackedPsmIds = new HashSet<string>(StringComparer.Ordinal);

        internal static void ConfigureTrackedPsmIds(IEnumerable<string> ids)
        {
            trackedPsmIds = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        // Must run on the game thread. Return null until a controlled pawn and role data exist.
        internal static WukongGameState Read()
        {
            UWorld world = GCHelper.FindRef(FGlobals.GWorld)?.Managed as UWorld;
            if (world == null) return null;
            var controller = UGSE_EngineFuncLib.GetFirstLocalPlayerController(world);
            var pawn = controller?.GetControlledPawn();
            if (pawn == null) return null;

            // Verified on game version 1.0.21.23831. Name warns that this API may disappear.
            if (roleMethod == null || !roleMethod.DeclaringType.IsInstanceOfType(controller))
                roleMethod = controller.GetType().GetMethod("GetReadOnlyDataTodoRemove",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var role = roleMethod?.Invoke(controller, null) as IBPC_PlayerRoleData;
            if (role == null) return null;

            var roleCs = role.RoleData?.RoleCs;
            var chapter = roleCs?.Chapter?.CurChapter;
            var location = pawn.GetActorLocation();
            var state = new WukongGameState
            {
                RawChapter = chapter == null ? (int?)null : Convert.ToInt32(chapter),
                MapId = role.MapId,
                AreaId = role.MapAreaId,
                NewGamePlusCount = role.GetNewGamePlusCount(),
                X = Convert.ToDouble(location.X),
                Y = Convert.ToDouble(location.Y),
                Z = Convert.ToDouble(location.Z)
            };
            var quests = roleCs?.Task?.QuestList;
            if (quests != null)
            {
                state.QuestStateAvailable = true;
                foreach (var quest in quests)
                    state.QuestStages[quest.Id] = quest.Stage.ToString();
            }
            // Candidate state sources. Exact item and interaction ID meanings
            // require before/after validation in the installed game version.
            var items = roleCs?.Bag?.ItemList;
            if (items != null)
            {
                state.ItemStateAvailable = true;
                foreach (var item in items)
                {
                    state.ItemQuantities[item.ItemId] = item.Num + "/" + item.StoreNum;
                    state.ItemCounts[item.ItemId] = item.Num;
                }
            }
            var equips = roleCs?.Bag?.EquipList;
            if (equips != null)
            {
                state.EquipStateAvailable = true;
                foreach (var equip in equips)
                    state.EquipIds.Add(equip.EquipId);
            }
            var interactions = roleCs?.Interaction?.InteractionFuncList;
            if (interactions != null)
                foreach (var id in interactions)
                    state.InteractionIds.Add(id);
            ReadWorldInteractions(world, state);
            ReadPsmArchive(world, state);
            return state;
        }

        private static void ReadWorldInteractions(UWorld world, WukongGameState state)
        {
            try
            {
                // Private manager property found in the installed reference assembly.
                // Availability and live updates still require in-game validation.
                var manager = BIS_TaskManager.Get(world);
                var data = manager == null ? null : TaskDataProperty?.GetValue(manager) as IBIC_TaskData;
                var records = data?.GetInteractionRecordList();
                if (records == null) return;
                state.WorldInteractionStateAvailable = true;
                foreach (var record in records)
                    state.WorldInteractionSteps[record.Key] = record.Value;
            }
            catch (Exception ex)
            {
                if (taskSourceErrorLogged) return;
                taskSourceErrorLogged = true;
                TraceLog.Write("[WukongGuard] world interaction source unavailable: " + ex);
            }
        }

        private static void ReadPsmArchive(UWorld world, WukongGameState state)
        {
            if (trackedPsmIds.Count == 0) return;
            try
            {
                var manager = BIS_StateMachineManager.Get(world);
                var data = manager == null ? null : StateMachineDataProperty?.GetValue(manager) as IBIC_StateMachineData;
                var graphs = data?.ArchiveData?.PsmArchiveData;
                if (graphs == null) return;
                state.PsmStateAvailable = true;
                foreach (var graph in graphs)
                {
                    if (!trackedPsmIds.Contains(graph.PsmId)) continue;
                    foreach (var node in graph.NodeData)
                        state.PsmNodeStates[graph.PsmId + "/" + node.UniqueId] = node.ActivationState.ToString();
                }
            }
            catch (Exception ex)
            {
                if (psmSourceErrorLogged) return;
                psmSourceErrorLogged = true;
                TraceLog.Write("[WukongGuard] state machine source unavailable: " + ex);
            }
        }
    }
}
