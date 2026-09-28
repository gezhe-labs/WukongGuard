using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using CSharpModBase;
using WukongGuard.Core;

namespace WukongGuard
{
    internal static class GuardMonitor
    {
        private static Timer timer;
        private static RuleEngine engine;
        private static AutoExperienceEngine experience;
        private static int queued;
        private static volatile bool active;
        private static string lastProgress;
        private static Dictionary<int, string> lastQuestStages;
        private static Dictionary<int, string> lastItemQuantities;
        private static HashSet<int> lastEquipIds;
        private static HashSet<int> lastInteractionIds;
        private static Dictionary<int, int> lastWorldInteractionSteps;
        private static Dictionary<string, string> lastPsmNodeStates;
        private static string lastArchiveAvailability;
        private static bool errorLogged;
        private static WukongGameState latest;
        private static int activeRuleCount;
        private static string lastStatus;
        private static bool developmentMode;
        private static bool needsQuestState;
        private static bool needsItemState;
        private static bool needsEquipState;
        private static bool needsWorldInteractionState;
        private static bool needsPsmState;
        private static volatile bool hiddenAreaHints;

        internal static void Start(IReadOnlyList<MissableRule> previewRules, bool developerDiagnostics)
        {
            developmentMode = developerDiagnostics;
            errorLogged = false;
            var activeRules = RuleLoader.Load();
            activeRuleCount = activeRules.Count(rule => rule.Category == "missable");
            hiddenAreaHints = GuardSettings.HiddenAreaHints();
            engine = new RuleEngine(activeRules);
            needsQuestState = activeRules.Any(rule => rule.QuestId.HasValue
                || rule.Conditions != null && rule.Conditions.Any(condition => condition.Source == "quest"));
            needsItemState = activeRules.Any(rule => rule.Conditions != null
                && rule.Conditions.Any(condition => condition.Source == "item"));
            needsEquipState = activeRules.Any(rule => rule.Conditions != null
                && rule.Conditions.Any(condition => condition.Source == "equip"));
            needsWorldInteractionState = activeRules.Any(rule => rule.Conditions != null
                && rule.Conditions.Any(condition => condition.Source == "world_interaction"));
            needsPsmState = activeRules.Any(rule => rule.Conditions != null
                && rule.Conditions.Any(condition => condition.Source == "psm"));
            var psmIds = activeRules.SelectMany(rule => rule.Conditions ?? new List<SignalCondition>())
                .Where(condition => condition.Source == "psm")
                .Select(condition => condition.Key.Split('/')[0]).ToList();
            if (developmentMode) psmIds.Add("2030001");
            GameStateAdapter.ConfigureTrackedPsmIds(psmIds);
            experience = new AutoExperienceEngine(previewRules,
                RuleLoader.LoadAutomaticExperienceEnabled());
            active = true;
            timer = new Timer(QueueSample, null, 1000, 1000);
            TraceLog.Write("[WukongGuard] runtime monitor started");
            TraceLog.Write("[WukongGuard] automatic experience=" + experience.Enabled);
            PublishStatus("等待角色数据；正式规则" + activeRuleCount + "条");
        }

        internal static void Stop()
        {
            active = false;
            PublishStatus("插件已停止");
            timer?.Dispose();
            timer = null;
            experience = null;
            latest = null;
            lastProgress = null;
            lastQuestStages = null;
            lastItemQuantities = null;
            lastEquipIds = null;
            lastInteractionIds = null;
            lastWorldInteractionSteps = null;
            lastPsmNodeStates = null;
            lastArchiveAvailability = null;
            lastStatus = null;
            developmentMode = false;
            needsQuestState = false;
            needsItemState = false;
            needsEquipState = false;
            needsWorldInteractionState = false;
            needsPsmState = false;
            hiddenAreaHints = false;
            Interlocked.Exchange(ref queued, 0);
        }

        internal static void Snapshot()
        {
            var state = latest;
            TraceLog.Write(state == null
                ? "[WukongGuard] state unavailable (menu, loading, or API changed)"
                : $"[WukongGuard] state chapter={state.RawChapter} map={state.MapId} area={state.AreaId} ng+={state.NewGamePlusCount} quests={state.QuestStages.Count} equips={state.EquipIds.Count} lantern={state.EquipIds.Contains(16030)} world_records={state.WorldInteractionSteps.Count} psm_nodes={state.PsmNodeStates.Count} xyz={state.X:F1},{state.Y:F1},{state.Z:F1}");
        }

        internal static void DumpQuests()
        {
            var state = latest;
            if (state == null)
            {
                TraceLog.Write("[WukongGuard] quest snapshot unavailable");
                return;
            }
            TraceLog.Write("[WukongGuard] quest snapshot progress=" + state.ProgressKey
                + " xyz=" + $"{state.X:F1},{state.Y:F1},{state.Z:F1}"
                + " quests=" + string.Join(",", state.QuestStages.OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key + ":" + pair.Value)));
            TraceLog.Write("[WukongGuard] item snapshot progress=" + state.ProgressKey
                + " items=" + string.Join(",", state.ItemQuantities.OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key + ":" + pair.Value)));
            TraceLog.Write("[WukongGuard] interaction snapshot progress=" + state.ProgressKey
                + " ids=" + string.Join(",", state.InteractionIds.OrderBy(id => id)));
            TraceLog.Write("[WukongGuard] world interaction snapshot available="
                + state.WorldInteractionStateAvailable + " progress=" + state.ProgressKey
                + " records=" + string.Join(",", state.WorldInteractionSteps.OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key + ":" + pair.Value)));
            TraceLog.Write("[WukongGuard] psm candidate snapshot available=" + state.PsmStateAvailable
                + " progress=" + state.ProgressKey + " nodes="
                + string.Join(",", state.PsmNodeStates.Where(pair => pair.Key.StartsWith("2030001/", StringComparison.Ordinal))
                    .OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + pair.Value)));
        }

        internal static void ToggleExperience()
        {
            if (experience == null) return;
            experience.Enabled = !experience.Enabled;
            TraceLog.Write("[WukongGuard] automatic experience=" + experience.Enabled);
        }

        private static void QueueSample(object ignored)
        {
            if (!active || Interlocked.Exchange(ref queued, 1) != 0) return;
            try
            {
                hiddenAreaHints = GuardSettings.HiddenAreaHints();
                Utils.TryRunOnGameThread(() =>
                {
                    try { if (active) Sample(); }
                    finally { Interlocked.Exchange(ref queued, 0); }
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref queued, 0);
                LogError(ex);
            }
        }

        private static void Sample()
        {
            try
            {
                var state = GameStateAdapter.Read();
                latest = state;
                if (state == null)
                {
                    PublishStatus("等待角色数据；正式规则" + activeRuleCount + "条");
                    return;
                }
                bool missingRequiredSource = needsQuestState && !state.QuestStateAvailable
                    || needsItemState && !state.ItemStateAvailable
                    || needsEquipState && !state.EquipStateAvailable
                    || needsWorldInteractionState && !state.WorldInteractionStateAvailable
                    || needsPsmState && !state.PsmStateAvailable;
                PublishStatus(missingRequiredSource
                    ? "部分任务状态不可用；依赖该状态的提醒暂停"
                    : "实时读取正常；失效提醒" + activeRuleCount + "条；隐藏地区"
                        + (hiddenAreaHints ? "开启" : "关闭"));
                if (developmentMode && state.ProgressKey != lastProgress)
                {
                    lastProgress = state.ProgressKey;
                    TraceLog.Write("[WukongGuard] progress " + lastProgress);
                }
                if (developmentMode)
                {
                    GameDbCatalogProbe.Sample(state.MapId, state.AreaId);
                    LogQuestChanges(state);
                    LogSecondaryStateChanges(state);
                    LogArchiveStateChanges(state);
                }
                foreach (var rule in engine.Evaluate(state, hiddenAreaHints))
                {
                    TraceLog.Write("[WukongGuard] matched " + rule.Id);
                    var deliveryEngine = engine;
                    var deliveryCycle = state.NewGamePlusCount;
                    OverlayPublisher.Show(rule, delivered =>
                    {
                        if (delivered)
                            TraceLog.Write("[WukongGuard] delivered " + rule.Id);
                        else
                        {
                            deliveryEngine.RearmAfterDeliveryFailure(rule.Id, deliveryCycle, TimeSpan.FromSeconds(10));
                            TraceLog.Write("[WukongGuard] delivery failed; retry pending " + rule.Id);
                        }
                    });
                }
                var preview = experience?.Evaluate(state, DateTime.UtcNow);
                if (preview != null)
                {
                    TraceLog.Write("[WukongGuard] automatic experience preview " + preview.Id);
                    OverlayPublisher.Show(new MissableRule
                    {
                        Id = "auto_experience_" + preview.Id,
                        Spoilers = preview.Spoilers
                    });
                }
            }
            catch (Exception ex) { LogError(ex); }
        }

        private static void LogQuestChanges(WukongGameState state)
        {
            var current = state.QuestStages;
            if (lastQuestStages == null)
            {
                lastQuestStages = new Dictionary<int, string>(current);
                TraceLog.Write("[WukongGuard] quest baseline count=" + current.Count);
                return;
            }

            var changed = lastQuestStages.Keys.Union(current.Keys).OrderBy(id => id)
                .Where(id => !lastQuestStages.TryGetValue(id, out var previous)
                    || !current.TryGetValue(id, out var now)
                    || !string.Equals(previous, now, StringComparison.Ordinal))
                .Select(id => id + ":"
                    + (lastQuestStages.TryGetValue(id, out var previous) ? previous : "<missing>")
                    + "->"
                    + (current.TryGetValue(id, out var now) ? now : "<missing>"))
                .ToArray();
            if (changed.Length > 0)
            {
                TraceLog.Write("[WukongGuard] quest changes progress=" + state.ProgressKey
                    + " xyz=" + $"{state.X:F1},{state.Y:F1},{state.Z:F1}"
                    + " " + string.Join(",", changed));
                lastQuestStages = new Dictionary<int, string>(current);
            }
        }

        private static void LogSecondaryStateChanges(WukongGameState state)
        {
            if (state.MapId.GetValueOrDefault() == 0) return;
            if (lastItemQuantities == null)
            {
                lastItemQuantities = new Dictionary<int, string>(state.ItemQuantities);
                lastEquipIds = new HashSet<int>(state.EquipIds);
                lastInteractionIds = new HashSet<int>(state.InteractionIds);
                TraceLog.Write("[WukongGuard] secondary baseline items=" + lastItemQuantities.Count
                    + " equips=" + lastEquipIds.Count
                    + " lantern=" + lastEquipIds.Contains(16030)
                    + " interactions=" + lastInteractionIds.Count);
                return;
            }

            var itemChanges = lastItemQuantities.Keys.Union(state.ItemQuantities.Keys)
                .OrderBy(id => id)
                .Where(id => !lastItemQuantities.TryGetValue(id, out var previous)
                    || !state.ItemQuantities.TryGetValue(id, out var now)
                    || !string.Equals(previous, now, StringComparison.Ordinal))
                .Select(id => id + ":"
                    + (lastItemQuantities.TryGetValue(id, out var previous) ? previous : "<missing>")
                    + "->"
                    + (state.ItemQuantities.TryGetValue(id, out var now) ? now : "<missing>"))
                .ToArray();
            var addedInteractions = state.InteractionIds.Except(lastInteractionIds).OrderBy(id => id).ToArray();
            var removedInteractions = lastInteractionIds.Except(state.InteractionIds).OrderBy(id => id).ToArray();
            var addedEquips = state.EquipIds.Except(lastEquipIds).OrderBy(id => id).ToArray();
            var removedEquips = lastEquipIds.Except(state.EquipIds).OrderBy(id => id).ToArray();
            if (itemChanges.Length == 0 && addedInteractions.Length == 0 && removedInteractions.Length == 0
                && addedEquips.Length == 0 && removedEquips.Length == 0)
                return;

            TraceLog.Write("[WukongGuard] secondary changes progress=" + state.ProgressKey
                + " xyz=" + $"{state.X:F1},{state.Y:F1},{state.Z:F1}"
                + " items=" + string.Join(",", itemChanges)
                + " equips+=" + string.Join(",", addedEquips)
                + " equips-=" + string.Join(",", removedEquips)
                + " interactions+=" + string.Join(",", addedInteractions)
                + " interactions-=" + string.Join(",", removedInteractions));
            lastItemQuantities = new Dictionary<int, string>(state.ItemQuantities);
            lastEquipIds = new HashSet<int>(state.EquipIds);
            lastInteractionIds = new HashSet<int>(state.InteractionIds);
        }

        private static void LogArchiveStateChanges(WukongGameState state)
        {
            var availability = "world=" + state.WorldInteractionStateAvailable
                + " psm=" + state.PsmStateAvailable;
            if (availability != lastArchiveAvailability)
            {
                lastArchiveAvailability = availability;
                TraceLog.Write("[WukongGuard] archive source " + availability);
            }
            if (state.WorldInteractionStateAvailable)
            {
                if (lastWorldInteractionSteps == null)
                {
                    TraceLog.Write("[WukongGuard] world interaction baseline count="
                        + state.WorldInteractionSteps.Count);
                    if (developmentMode)
                        TraceLog.Write("[WukongGuard] calibration world 2003102="
                            + (state.WorldInteractionSteps.TryGetValue(2003102, out var step)
                                ? step.ToString() : "<missing>"));
                }
                else
                {
                    var changes = lastWorldInteractionSteps.Keys.Union(state.WorldInteractionSteps.Keys)
                        .OrderBy(id => id)
                        .Where(id => !lastWorldInteractionSteps.TryGetValue(id, out var before)
                            || !state.WorldInteractionSteps.TryGetValue(id, out var after)
                            || before != after)
                        .Select(id => id + ":"
                            + (lastWorldInteractionSteps.TryGetValue(id, out var before) ? before.ToString() : "<missing>")
                            + "->"
                            + (state.WorldInteractionSteps.TryGetValue(id, out var after) ? after.ToString() : "<missing>"))
                        .ToArray();
                    if (changes.Length > 0)
                        TraceLog.Write("[WukongGuard] world interaction changes progress=" + state.ProgressKey
                            + " " + string.Join(",", changes));
                }
                lastWorldInteractionSteps = new Dictionary<int, int>(state.WorldInteractionSteps);
            }
            else lastWorldInteractionSteps = null;

            if (state.PsmStateAvailable)
            {
                if (lastPsmNodeStates == null)
                {
                    TraceLog.Write("[WukongGuard] psm baseline count=" + state.PsmNodeStates.Count);
                    if (developmentMode)
                        TraceLog.Write("[WukongGuard] calibration psm 2030001 nodes="
                            + string.Join(",", state.PsmNodeStates
                                .Where(pair => pair.Key.StartsWith("2030001/", StringComparison.Ordinal))
                                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                .Select(pair => pair.Key + ":" + pair.Value)));
                }
                else
                {
                    var changes = lastPsmNodeStates.Keys.Union(state.PsmNodeStates.Keys)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .Where(id => !lastPsmNodeStates.TryGetValue(id, out var before)
                            || !state.PsmNodeStates.TryGetValue(id, out var after)
                            || !string.Equals(before, after, StringComparison.Ordinal))
                        .Select(id => id + ":"
                            + (lastPsmNodeStates.TryGetValue(id, out var before) ? before : "<missing>")
                            + "->"
                            + (state.PsmNodeStates.TryGetValue(id, out var after) ? after : "<missing>"))
                        .ToArray();
                    if (changes.Length > 0)
                        TraceLog.Write("[WukongGuard] psm changes progress=" + state.ProgressKey
                            + " " + string.Join(",", changes.Take(40))
                            + (changes.Length > 40 ? " ... total=" + changes.Length : ""));
                }
                lastPsmNodeStates = new Dictionary<string, string>(state.PsmNodeStates, StringComparer.Ordinal);
            }
            else lastPsmNodeStates = null;
        }

        private static void LogError(Exception ex)
        {
            if (errorLogged) return;
            errorLogged = true;
            TraceLog.Write("[WukongGuard] runtime read failed; alerts fail closed: " + ex);
            PublishStatus("运行时读取失败；提醒暂停");
        }

        private static void PublishStatus(string status)
        {
            if (status == lastStatus) return;
            lastStatus = status;
            OverlayPublisher.ReportStatus(status);
        }
    }
}
