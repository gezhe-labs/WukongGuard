using System.Collections.Generic;

namespace WukongGuard.Core
{
    public sealed class RuleFile
    {
        public List<MissableRule> Rules { get; set; } = new List<MissableRule>();
    }

    public sealed class MissableRule
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public string Category { get; set; } = "missable";
        public int? RawChapter { get; set; }
        public int? MapId { get; set; }
        public int? AreaId { get; set; }
        public int? QuestId { get; set; }
        public string QuestStage { get; set; }
        // A location check only asserts proximity to a documented lockout point.
        // Its copy must be conditional because completion is not mapped yet.
        public bool LocationCheckOnly { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public double? Radius { get; set; }
        // Optional movement gate for shrine-adjacent hidden-area hints.
        public int MovementSecondsBeforeTrigger { get; set; }
        public List<SignalCondition> Conditions { get; set; } = new List<SignalCondition>();
        public string[] Spoilers { get; set; }
    }

    public sealed class SignalCondition
    {
        public string Source { get; set; }
        public string Key { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
    }
}
