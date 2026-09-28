using System;
using System.Collections.Generic;

namespace WukongGuard.Core
{
    public sealed class WukongGameState
    {
        public int? RawChapter { get; set; }
        public int? MapId { get; set; }
        public int? AreaId { get; set; }
        public int? NewGamePlusCount { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Z { get; set; }
        public Dictionary<int, string> QuestStages { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> ItemQuantities { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, int> ItemCounts { get; set; } = new Dictionary<int, int>();
        public HashSet<int> EquipIds { get; set; } = new HashSet<int>();
        public HashSet<int> InteractionIds { get; set; } = new HashSet<int>();
        public Dictionary<int, int> WorldInteractionSteps { get; set; } = new Dictionary<int, int>();
        public Dictionary<string, string> PsmNodeStates { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool QuestStateAvailable { get; set; }
        public bool ItemStateAvailable { get; set; }
        public bool EquipStateAvailable { get; set; }
        public bool WorldInteractionStateAvailable { get; set; }
        public bool PsmStateAvailable { get; set; }
        public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;

        public string ProgressKey => $"{RawChapter?.ToString() ?? "?"}/{MapId?.ToString() ?? "?"}/{AreaId?.ToString() ?? "?"}";
    }
}
