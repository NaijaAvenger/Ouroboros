using System;

namespace Ouroboros.Core
{
    /// <summary>
    /// Pure match-rule arithmetic (v0.9). No Unity or Fusion types, so every function here can be unit-tested
    /// outside the engine (see Tools/tests). The networked behaviours own the state and call into these;
    /// change a rule here and its tests in the same commit.
    /// </summary>
    public static class MatchRules
    {
        /// <summary>
        /// Index of the winning team, or -1 when no team has players. Highest score wins; ties go to the team
        /// with more banked loot, then more extracted members, then the lower index.
        /// </summary>
        public static int PickWinner(int[] scores, int[] loot, int[] extracted, bool[] hasPlayers)
        {
            if (scores == null) return -1;
            int best = -1;
            int bestScore = int.MinValue, bestLoot = int.MinValue, bestExtracted = int.MinValue;

            for (int i = 0; i < scores.Length; i++)
            {
                if (hasPlayers != null && i < hasPlayers.Length && !hasPlayers[i]) continue;

                int score = scores[i];
                int teamLoot = loot != null && i < loot.Length ? loot[i] : 0;
                int teamExtracted = extracted != null && i < extracted.Length ? extracted[i] : 0;

                bool better = score > bestScore
                              || (score == bestScore && teamLoot > bestLoot)
                              || (score == bestScore && teamLoot == bestLoot && teamExtracted > bestExtracted);
                if (better)
                {
                    best = i;
                    bestScore = score; bestLoot = teamLoot; bestExtracted = teamExtracted;
                }
            }
            return best;
        }

        /// <summary>Alarm tier for a level: 0 Quiet, 1 Suspicious, 2 Alert, 3 Lockdown. Thresholds are inclusive.</summary>
        public static int AlarmTier(float level, float suspiciousThreshold, float alertThreshold, float lockdownThreshold)
        {
            if (level >= lockdownThreshold) return 3;
            if (level >= alertThreshold) return 2;
            if (level >= suspiciousThreshold) return 1;
            return 0;
        }

        /// <summary>
        /// Splits <paramref name="total"/> loot between <paramref name="workers"/>. Everyone gets
        /// <paramref name="share"/>; the first worker also gets <paramref name="remainder"/> so nothing is lost.
        /// </summary>
        public static void SplitLoot(int total, int workers, out int share, out int remainder)
        {
            if (workers <= 0)
            {
                share = total;
                remainder = 0;
                return;
            }
            share = total / workers;
            remainder = total - share * workers;
        }

        /// <summary>Vault cracking speed: each extra worker adds 25%, up to three extra (1.0 .. 1.75).</summary>
        public static float CrackRate(int workers)
        {
            int extra = Math.Max(0, Math.Min(workers - 1, 3));
            return 1f + 0.25f * extra;
        }

        /// <summary>
        /// Movement multiplier from carried loot and a carried case. Loot slows linearly up to
        /// <paramref name="maxSlowdown"/> at <paramref name="lootReference"/>; a case multiplies on top;
        /// the result never drops below 0.3.
        /// </summary>
        public static float CarrySpeedMultiplier(int carriedLoot, int lootReference, float maxSlowdown, bool carryingCase, float caseMultiplier)
        {
            float load = lootReference > 0 ? Clamp((float)carriedLoot / lootReference, 0f, 1f) : 0f;
            float m = 1f - maxSlowdown * load;
            if (carryingCase) m *= caseMultiplier;
            return Clamp(m, 0.3f, 1f);
        }

        /// <summary>Index of the lowest set bit of <paramref name="flags"/>, or -1 when none is set.</summary>
        public static int LowestSetBit(int flags)
        {
            if (flags <= 0) return -1;
            int bit = 0;
            while ((flags & 1) == 0) { flags >>= 1; bit++; }
            return bit;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
    }
}
