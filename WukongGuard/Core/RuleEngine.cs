using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WukongGuard.Core
{
    public sealed class RuleEngine
    {
        private readonly IReadOnlyList<MissableRule> rules;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> retryAfterUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, MovementWindow> movementWindows = new Dictionary<string, MovementWindow>(StringComparer.Ordinal);
        private readonly object gate = new object();

        private sealed class MovementWindow
        {
            public DateTime LastObservedAtUtc;
            public double StartX, StartY, StartZ;
            public double LastX, LastY, LastZ;
            public double MovingSeconds;
        }

        public RuleEngine(IEnumerable<MissableRule> rules)
        {
            this.rules = (rules ?? Enumerable.Empty<MissableRule>()).ToList();
        }

        public IReadOnlyList<MissableRule> Evaluate(WukongGameState state, bool hiddenAreaHints = false)
        {
            var matches = new List<MissableRule>();
            if (state == null) return matches;
            lock (gate)
            {
                foreach (var rule in rules)
                {
                    if (!IsValid(rule)) continue;
                    string cycleKey = CycleKey(rule.Id, state.NewGamePlusCount);
                    if (fired.Contains(cycleKey) && IsOutsideRearmZone(rule, state))
                    {
                        fired.Remove(cycleKey);
                        movementWindows.Remove(cycleKey);
                    }
                    if (rule.Category == "hidden_area" && !hiddenAreaHints)
                    {
                        movementWindows.Remove(cycleKey);
                        continue;
                    }
                    if (fired.Contains(cycleKey)
                        || retryAfterUtc.TryGetValue(cycleKey, out var retryAt) && DateTime.UtcNow < retryAt)
                        continue;
                    if (!Matches(rule, state))
                    {
                        movementWindows.Remove(cycleKey);
                        continue;
                    }
                    if (!HasCompletedMovement(rule, state, cycleKey)) continue;
                    fired.Add(cycleKey);
                    matches.Add(rule);
                }
            }
            return matches;
        }

        public void RearmAfterDeliveryFailure(string id, int? newGamePlusCount, TimeSpan delay)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (gate)
            {
                string cycleKey = CycleKey(id, newGamePlusCount);
                fired.Remove(cycleKey);
                retryAfterUtc[cycleKey] = DateTime.UtcNow.Add(delay);
            }
        }

        private static string CycleKey(string id, int? newGamePlusCount)
            => (newGamePlusCount.HasValue
                ? newGamePlusCount.Value.ToString(CultureInfo.InvariantCulture) : "?")
                + "/" + id;

        private bool HasCompletedMovement(MissableRule rule, WukongGameState state, string key)
        {
            if (rule.MovementSecondsBeforeTrigger == 0) return true;
            var now = state.ObservedAtUtc;
            if (!movementWindows.TryGetValue(key, out var window))
            {
                movementWindows[key] = new MovementWindow
                {
                    LastObservedAtUtc = now,
                    StartX = state.X.Value, StartY = state.Y.Value, StartZ = state.Z.Value,
                    LastX = state.X.Value, LastY = state.Y.Value, LastZ = state.Z.Value
                };
                return false;
            }
            double seconds = (now - window.LastObservedAtUtc).TotalSeconds;
            double dx = state.X.Value - window.LastX;
            double dy = state.Y.Value - window.LastY;
            double dz = state.Z.Value - window.LastZ;
            if (seconds > 2.5)
            {
                // A loading screen or a stalled sample cannot count as walking time.
                window.MovingSeconds = 0;
                window.StartX = state.X.Value;
                window.StartY = state.Y.Value;
                window.StartZ = state.Z.Value;
            }
            else if (seconds > 0 && dx * dx + dy * dy + dz * dz >= 30 * 30)
                window.MovingSeconds += seconds;
            window.LastObservedAtUtc = now;
            window.LastX = state.X.Value;
            window.LastY = state.Y.Value;
            window.LastZ = state.Z.Value;
            dx = state.X.Value - window.StartX;
            dy = state.Y.Value - window.StartY;
            dz = state.Z.Value - window.StartZ;
            return window.MovingSeconds >= rule.MovementSecondsBeforeTrigger
                && dx * dx + dy * dy + dz * dz >= 150 * 150;
        }

        private static bool IsOutsideRearmZone(MissableRule rule, WukongGameState state)
        {
            // Loading screens and temporarily missing coordinates do not count as leaving.
            if (!state.RawChapter.HasValue || !state.MapId.HasValue || state.MapId.Value == 0)
                return false;
            if (state.RawChapter != rule.RawChapter || state.MapId != rule.MapId)
                return true;
            if (rule.AreaId.HasValue)
            {
                if (!state.AreaId.HasValue) return false;
                if (state.AreaId != rule.AreaId) return true;
            }
            if (!state.X.HasValue || !state.Y.HasValue || !state.Z.HasValue)
                return false;
            double dx = state.X.Value - rule.X.Value;
            double dy = state.Y.Value - rule.Y.Value;
            double dz = state.Z.Value - rule.Z.Value;
            double exitRadius = rule.Radius.Value + Math.Max(300, rule.Radius.Value * 0.1);
            return dx * dx + dy * dy + dz * dz > exitRadius * exitRadius;
        }

        public static bool IsValid(MissableRule rule)
        {
            return rule != null && rule.Enabled && !string.IsNullOrWhiteSpace(rule.Id)
                && (rule.Category == "missable" || rule.Category == "hidden_area")
                && rule.RawChapter.HasValue && rule.MapId.HasValue
                && (rule.QuestId.HasValue == !string.IsNullOrWhiteSpace(rule.QuestStage))
                && (rule.LocationCheckOnly || rule.QuestId.HasValue || rule.Conditions?.Count > 0)
                && (!rule.LocationCheckOnly || !rule.QuestId.HasValue && (rule.Conditions == null || rule.Conditions.Count == 0))
                && (rule.Conditions == null || rule.Conditions.All(IsValidCondition))
                && rule.X.HasValue && rule.Y.HasValue && rule.Z.HasValue
                && rule.Radius.HasValue && rule.Radius.Value > 0
                && rule.Radius.Value <= 5000
                && rule.MovementSecondsBeforeTrigger >= 0
                && rule.MovementSecondsBeforeTrigger <= 10
                && (rule.MovementSecondsBeforeTrigger == 0 || rule.Category == "hidden_area")
                && rule.Spoilers != null && rule.Spoilers.Length == 4
                && rule.Spoilers.All(s => !string.IsNullOrWhiteSpace(s));
        }

        private static bool Matches(MissableRule rule, WukongGameState state)
        {
            if (state.RawChapter != rule.RawChapter || state.MapId != rule.MapId
                || rule.AreaId.HasValue && state.AreaId != rule.AreaId)
                return false;
            if (rule.QuestId.HasValue &&
                (!state.QuestStages.TryGetValue(rule.QuestId.Value, out var stage)
                || !string.Equals(stage, rule.QuestStage, StringComparison.Ordinal))) return false;
            if (rule.Conditions != null && rule.Conditions.Any(condition => !MatchesCondition(condition, state)))
                return false;
            if (!state.X.HasValue || !state.Y.HasValue || !state.Z.HasValue) return false;
            double dx = state.X.Value - rule.X.Value;
            double dy = state.Y.Value - rule.Y.Value;
            double dz = state.Z.Value - rule.Z.Value;
            return dx * dx + dy * dy + dz * dz <= rule.Radius.Value * rule.Radius.Value;
        }

        private static bool IsValidCondition(SignalCondition condition)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.Key)
                || condition.Key.Length > 100) return false;
            bool numericSource = condition.Source == "item" || condition.Source == "world_interaction";
            bool idSource = numericSource || condition.Source == "equip";
            if (condition.Source != "quest" && !idSource && condition.Source != "psm") return false;
            if (condition.Source != "psm" && (!int.TryParse(condition.Key, NumberStyles.None,
                CultureInfo.InvariantCulture, out var key) || key <= 0)) return false;
            if (condition.Source == "psm" && !condition.Key.Contains("/")) return false;
            if (condition.Operator == "absent") return condition.Value == null;
            if (string.IsNullOrWhiteSpace(condition.Value)) return false;
            if (condition.Operator == "equals") return true;
            if (condition.Operator == "not_equals" && condition.Source == "quest") return true;
            if ((condition.Operator == "at_least" || condition.Operator == "at_most") && numericSource)
                return int.TryParse(condition.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
            return false;
        }

        private static bool MatchesCondition(SignalCondition condition, WukongGameState state)
        {
            bool available;
            bool present;
            string actual;
            int key;
            switch (condition.Source)
            {
                case "quest":
                    available = state.QuestStateAvailable;
                    present = state.QuestStages.TryGetValue(int.Parse(condition.Key, CultureInfo.InvariantCulture), out actual);
                    break;
                case "item":
                    available = state.ItemStateAvailable;
                    key = int.Parse(condition.Key, CultureInfo.InvariantCulture);
                    present = state.ItemCounts.TryGetValue(key, out var count) && count > 0;
                    actual = count.ToString(CultureInfo.InvariantCulture);
                    break;
                case "equip":
                    available = state.EquipStateAvailable;
                    present = state.EquipIds.Contains(int.Parse(condition.Key, CultureInfo.InvariantCulture));
                    actual = present ? "present" : null;
                    break;
                case "world_interaction":
                    available = state.WorldInteractionStateAvailable;
                    present = state.WorldInteractionSteps.TryGetValue(int.Parse(condition.Key, CultureInfo.InvariantCulture), out var step);
                    actual = step.ToString(CultureInfo.InvariantCulture);
                    break;
                case "psm":
                    available = state.PsmStateAvailable;
                    present = state.PsmNodeStates.TryGetValue(condition.Key, out actual);
                    break;
                default: return false;
            }
            if (!available) return false;
            if (condition.Operator == "absent") return !present;
            if (condition.Operator == "not_equals")
                return state.QuestStages.Count > 0
                    && (!present || !string.Equals(actual, condition.Value, StringComparison.Ordinal));
            if (!present) return false;
            if (condition.Operator == "equals") return string.Equals(actual, condition.Value, StringComparison.Ordinal);
            if (!int.TryParse(actual, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                || !int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
                return false;
            return condition.Operator == "at_least" ? number >= expected : number <= expected;
        }
    }
}
