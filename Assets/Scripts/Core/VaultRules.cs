using System;

namespace Ouroboros.Core
{
    /// <summary>(v0.21) The ways a Grand Vault's door can be opened. A vault offers one or more of them.</summary>
    [Flags]
    public enum VaultMethod
    {
        None = 0,
        /// <summary>Kill the vault's guards; the last one drops its key. Carry the key to the door.</summary>
        Key = 1,
        /// <summary>Start the drill at the door, then hold the doorway until it is through. Everyone is told.</summary>
        Drill = 2,
        /// <summary>Crack the lock at the console beside the door: the sign and pulse puzzles. Quiet.</summary>
        Crack = 4,
        All = Key | Drill | Crack
    }

    /// <summary>(v0.21) Where a Grand Vault's key is.</summary>
    public enum VaultKeyState : byte
    {
        /// <summary>This vault has no key.</summary>
        None = 0,
        /// <summary>Still on one of the vault's guards.</summary>
        Guarded = 1,
        /// <summary>On the floor.</summary>
        Dropped = 2,
        /// <summary>In a player's pocket. It falls again if they die.</summary>
        Carried = 3,
        /// <summary>The vault is open: the key is spent.</summary>
        Used = 4
    }

    /// <summary>(v0.21) How a Grand Vault came to be open, for the feed.</summary>
    public enum VaultOpening : byte { Key = 0, Drill = 1, Crack = 2, Wall = 3, TimeLock = 4 }

    /// <summary>
    /// (v0.21) What a crew takes from a Grand Vault besides money: one perk per vault, claimed by the first crew
    /// to reach it, held by the whole crew for the rest of the match.
    /// </summary>
    public enum VaultPerk
    {
        None = 0,
        /// <summary>The crew takes less damage.</summary>
        IronSkin = 1,
        /// <summary>The crew deals more damage.</summary>
        HotShots = 2,
        /// <summary>Health comes back sooner and faster.</summary>
        SecondWind = 3,
        /// <summary>Reviving, cashing out, hijacking and starting a drill are all quicker.</summary>
        QuickHands = 4
    }

    /// <summary>
    /// (v0.21) Rules for Grand Vaults: the drill, the key, and what the perks do. No Unity or Fusion types, so
    /// the unit tests can run them.
    /// </summary>
    public static class VaultRules
    {
        public const float IronSkinDamageTaken = 0.8f;
        public const float HotShotsDamageDealt = 1.15f;
        public const float SecondWindDelay = 0.5f;
        public const float SecondWindRate = 2f;
        public const float QuickHandsTime = 0.6f;

        // ---- perks, held by a crew as a bit mask

        public static int PerkBit(VaultPerk perk) => perk == VaultPerk.None ? 0 : 1 << ((int)perk - 1);
        public static bool Has(int mask, VaultPerk perk) => perk != VaultPerk.None && (mask & PerkBit(perk)) != 0;
        public static int With(int mask, VaultPerk perk) => mask | PerkBit(perk);

        /// <summary>Multiplier on damage a crew with <paramref name="mask"/> takes.</summary>
        public static float DamageTaken(int mask) => Has(mask, VaultPerk.IronSkin) ? IronSkinDamageTaken : 1f;
        /// <summary>Multiplier on damage a crew with <paramref name="mask"/> deals.</summary>
        public static float DamageDealt(int mask) => Has(mask, VaultPerk.HotShots) ? HotShotsDamageDealt : 1f;
        /// <summary>Multiplier on the wait before health starts coming back.</summary>
        public static float RegenDelay(int mask) => Has(mask, VaultPerk.SecondWind) ? SecondWindDelay : 1f;
        /// <summary>Multiplier on how fast health comes back.</summary>
        public static float RegenRate(int mask) => Has(mask, VaultPerk.SecondWind) ? SecondWindRate : 1f;
        /// <summary>Multiplier on the seconds a held action takes: revive, deposit, hijack, starting a drill.</summary>
        public static float ActionTime(int mask) => Has(mask, VaultPerk.QuickHands) ? QuickHandsTime : 1f;

        public static string PerkName(VaultPerk perk)
        {
            switch (perk)
            {
                case VaultPerk.IronSkin:   return "Iron Skin";
                case VaultPerk.HotShots:   return "Hot Shots";
                case VaultPerk.SecondWind: return "Second Wind";
                case VaultPerk.QuickHands: return "Quick Hands";
                default:                   return "";
            }
        }

        /// <summary>One line saying what a perk does, in the numbers above.</summary>
        public static string PerkEffect(VaultPerk perk)
        {
            switch (perk)
            {
                case VaultPerk.IronSkin:   return $"your crew takes {Percent(1f - IronSkinDamageTaken)} less damage";
                case VaultPerk.HotShots:   return $"your crew deals {Percent(HotShotsDamageDealt - 1f)} more damage";
                case VaultPerk.SecondWind: return "your crew's health comes back in half the time and twice as fast";
                case VaultPerk.QuickHands: return $"your crew revives, cashes out, hijacks and drills {Percent(1f - QuickHandsTime)} faster";
                default:                   return "";
            }
        }

        private static string Percent(float fraction) => ((int)Math.Round(fraction * 100f)) + "%";

        // ---- the drill

        /// <summary>
        /// One step of a held action that fills while it is worked and empties when it is not, as a fraction
        /// from 0 to 1: starting the drill, pulling its plug, claiming a perk.
        /// </summary>
        public static float HoldStep(float progress, bool working, float deltaTime, float seconds, float drainPerSecond)
        {
            float p = Clamp01(progress);
            if (deltaTime <= 0f) return p;
            if (working) return seconds <= 0f ? 1f : Clamp01(p + deltaTime / seconds);
            return Clamp01(p - Math.Max(0f, drainPerSecond) * deltaTime);
        }

        /// <summary>
        /// Seconds of drilling done after one step. The drill runs on its own while one of the crew that started
        /// it is near the door, and runs backward while none is: it must be defended, not just started.
        /// </summary>
        public static float DrillStep(float secondsDone, bool crewNear, float deltaTime, float totalSeconds, float backwardRate)
        {
            float total = Math.Max(0f, totalSeconds);
            float done = Math.Max(0f, Math.Min(total, secondsDone));
            if (deltaTime <= 0f) return done;
            return crewNear ? Math.Min(total, done + deltaTime) : Math.Max(0f, done - Math.Max(0f, backwardRate) * deltaTime);
        }

        /// <summary>A drill that has run all the way back with nobody near is abandoned.</summary>
        public static bool DrillAbandoned(float secondsDone, bool crewNear) => secondsDone <= 0f && !crewNear;

        // ---- the key

        /// <summary>The key drops when a vault that had guards has none left.</summary>
        public static bool DetailCleared(int registered, int alive) => registered > 0 && alive <= 0;

        /// <summary>What a vault should hold so that emptying it is about one quota's worth.</summary>
        public static int HaulValue(int legendaryItems, int legendaryAverage, int rareItems, int rareAverage, int cashPiles, int cashPerPile)
        {
            return Math.Max(0, legendaryItems) * Math.Max(0, legendaryAverage) + Math.Max(0, rareItems) * Math.Max(0, rareAverage)
                 + Math.Max(0, cashPiles) * Math.Max(0, cashPerPile);
        }

        /// <summary>
        /// (v0.23) What a vault should be worth: a share of the quota, so that one vault and a little extra
        /// calls the getaway, and one vault alone does not.
        /// </summary>
        public static int HaulTarget(int quota, float shareOfQuota)
        {
            if (quota <= 0) return 0;
            return (int)Math.Round(quota * (double)Clamp01(shareOfQuota), MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Whether one more item fits the vault's target. The first item always does: a vault is never empty.
        /// </summary>
        public static bool FitsHaul(int laidSoFar, int itemValue, int target)
        {
            return laidSoFar <= 0 || laidSoFar + itemValue <= target;
        }

        /// <summary>
        /// How the rest of a vault's target is made up in cash: the number of piles, and what each is worth.
        /// Piles are kept under <paramref name="largestPile"/> and never more than <paramref name="mostPiles"/>.
        /// </summary>
        public static int CashPiles(int remaining, int largestPile, int mostPiles, out int perPile)
        {
            perPile = 0;
            if (remaining < 50 || mostPiles <= 0 || largestPile <= 0) return 0;
            int piles = Math.Min(mostPiles, (remaining + largestPile - 1) / largestPile);
            perPile = Math.Min(largestPile, remaining / piles);
            perPile -= perPile % 10;
            return perPile > 0 ? piles : 0;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
