using System;
using System.Collections.Generic;
using System.Linq;

namespace WukongGuard.Core
{
    // Automatically exercises the overlay from real runtime samples. It does not
    // infer quest completion or claim that the current location is a lockout.
    public sealed class AutoExperienceEngine
    {
        private readonly IReadOnlyList<MissableRule> candidates;
        private DateTime? firstSeenUtc;
        private DateTime? lastShownUtc;
        private double anchorX;
        private double anchorY;
        private double anchorZ;
        private int next;

        public bool Enabled { get; set; }

        public AutoExperienceEngine(IEnumerable<MissableRule> candidates, bool enabled)
        {
            this.candidates = (candidates ?? Enumerable.Empty<MissableRule>()).ToList();
            Enabled = enabled;
        }

        public MissableRule Evaluate(WukongGameState state, DateTime nowUtc)
        {
            if (!Enabled || next >= candidates.Count || state == null
                || !state.X.HasValue || !state.Y.HasValue || !state.Z.HasValue)
                return null;

            if (!firstSeenUtc.HasValue)
            {
                firstSeenUtc = nowUtc;
                RememberPosition(state);
                return null;
            }

            if (!lastShownUtc.HasValue)
            {
                if (nowUtc - firstSeenUtc.Value < TimeSpan.FromSeconds(5)) return null;
            }
            else
            {
                if (nowUtc - lastShownUtc.Value < TimeSpan.FromSeconds(20)) return null;
                double dx = state.X.Value - anchorX;
                double dy = state.Y.Value - anchorY;
                double dz = state.Z.Value - anchorZ;
                if (dx * dx + dy * dy + dz * dz < 100 * 100) return null;
            }

            lastShownUtc = nowUtc;
            RememberPosition(state);
            return candidates[next++];
        }

        private void RememberPosition(WukongGameState state)
        {
            anchorX = state.X.Value;
            anchorY = state.Y.Value;
            anchorZ = state.Z.Value;
        }
    }
}
