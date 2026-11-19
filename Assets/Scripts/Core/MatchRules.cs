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

        // ------------------------------------------------------------------
        // The Run (v0.10, milestone M2): quota race, getaway payouts, crew wipes, fair contests

        /// <summary>True when a crew is carrying enough to call the getaway. A quota of 0 disables the rule.</summary>
        public static bool QuotaReached(int crewCarried, int quota)
        {
            return quota > 0 && crewCarried >= quota;
        }

        /// <summary>
        /// Index of the crew carrying the most, or -1 when nobody carries anything. Ties go to the lower index.
        /// This is the crew everyone is told to hunt during the getaway.
        /// </summary>
        public static int RichestTeam(int[] carried, bool[] hasPlayers)
        {
            if (carried == null) return -1;
            int best = -1, bestValue = 0;
            for (int i = 0; i < carried.Length; i++)
            {
                if (hasPlayers != null && i < hasPlayers.Length && !hasPlayers[i]) continue;
                if (carried[i] > bestValue) { bestValue = carried[i]; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Value actually banked when loot leaves through an exit. The central getaway pays in full
        /// (multiplier 1); side exits pay a fraction.
        /// </summary>
        public static int BankedValue(int carriedValue, float payoutMultiplier)
        {
            if (carriedValue <= 0) return 0;
            float m = payoutMultiplier < 0f ? 0f : payoutMultiplier;
            return (int)Math.Round(carriedValue * (double)m, MidpointRounding.AwayFromZero);
        }

        // ------------------------------------------------------------------
        // Cash-out stations (v0.16): banking in the middle of a match, in public, and stealable

        /// <summary>
        /// What a crew has toward the getaway quota. With stations, money already banked counts as well as
        /// what is being carried, so cashing out never moves a crew away from the quota.
        /// </summary>
        public static int CrewHaul(int carried, int banked, bool countBanked)
        {
            return Math.Max(0, carried) + (countBanked ? Math.Max(0, banked) : 0);
        }

        /// <summary>Seconds left on a cash-out after its owners add more loot: a little longer, never past a full timer.</summary>
        public static float CashOutTimeAfterDeposit(float remaining, float fullSeconds, float extendSeconds)
        {
            float full = Math.Max(0f, fullSeconds);
            return Math.Min(full, Math.Max(0f, remaining) + Math.Max(0f, extendSeconds));
        }

        /// <summary>
        /// Seconds left after a hijack. The thief never inherits a cash-out that is about to finish: they must
        /// hold it for at least <paramref name="floorSeconds"/>.
        /// </summary>
        public static float CashOutTimeAfterHijack(float remaining, float floorSeconds)
        {
            return Math.Max(Math.Max(0f, remaining), Math.Max(0f, floorSeconds));
        }

        /// <summary>
        /// One step of a hijack, as a fraction from 0 to 1. It fills while a rival works the station and no owner
        /// stands at it, holds while it is contested, and drains when the rival lets go.
        /// </summary>
        public static float HijackStep(float progress, bool rivalWorking, bool ownerPresent, float deltaTime, float hijackSeconds, float drainPerSecond)
        {
            float p = Clamp01(progress);
            if (deltaTime <= 0f) return p;
            if (rivalWorking)
            {
                if (ownerPresent || hijackSeconds <= 0f) return p;
                return Clamp01(p + deltaTime / hijackSeconds);
            }
            return Clamp01(p - Math.Max(0f, drainPerSecond) * deltaTime);
        }

        /// <summary>A cash-out counts down only while nobody is in the middle of stealing it.</summary>
        public static float CashOutCountdown(float remaining, bool beingHijacked, float deltaTime)
        {
            if (beingHijacked || deltaTime <= 0f) return Math.Max(0f, remaining);
            return Math.Max(0f, remaining - deltaTime);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public enum WipeOutcome
        {
            /// <summary>The whole crew redeploys together after the wipe delay.</summary>
            Redeploy,
            /// <summary>No redeploys left: the crew is out of the match.</summary>
            Eliminated
        }

        /// <summary>
        /// What happens when the last living member of a crew dies. <paramref name="wipesSoFar"/> counts earlier
        /// wipes (not this one). A negative <paramref name="maxWipes"/> means unlimited redeploys.
        /// </summary>
        public static WipeOutcome ResolveCrewWipe(int wipesSoFar, int maxWipes)
        {
            if (maxWipes < 0) return WipeOutcome.Redeploy;
            return wipesSoFar < maxWipes ? WipeOutcome.Redeploy : WipeOutcome.Eliminated;
        }

        /// <summary>
        /// Team index to consider at step <paramref name="step"/> of a contest scan that starts at a different
        /// team every tick. Without this the lowest team id won every simultaneous claim on a vault or exit.
        /// </summary>
        public static int RotatedIndex(int tick, int step, int count)
        {
            if (count <= 0) return 0;
            int start = ((tick % count) + count) % count;
            return (start + step) % count;
        }

        /// <summary>
        /// Picks <paramref name="count"/> distinct indices out of <paramref name="poolSize"/>, the same way on
        /// every peer for the same seed. Fills <paramref name="result"/> and returns how many were written
        /// (fewer than asked when the pool is smaller).
        /// </summary>
        public static int PickDistinct(int seed, int poolSize, int count, int[] result)
        {
            if (result == null || poolSize <= 0 || count <= 0) return 0;
            int n = Math.Min(Math.Min(count, poolSize), result.Length);

            // Partial Fisher-Yates over a virtual 0..poolSize-1 array, tracking only the swapped slots.
            var swapped = new System.Collections.Generic.Dictionary<int, int>();
            uint state = unchecked((uint)seed * 2654435761u + 0x9E3779B9u);
            for (int i = 0; i < n; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5; // xorshift32
                int j = i + (int)(state % (uint)(poolSize - i));
                int atI = swapped.TryGetValue(i, out int vi) ? vi : i;
                int atJ = swapped.TryGetValue(j, out int vj) ? vj : j;
                result[i] = atJ;
                swapped[j] = atI;
                swapped[i] = atJ;
            }
            return n;
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
    }
}
