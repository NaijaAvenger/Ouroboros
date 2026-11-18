using System;
using System.Text;

namespace Ouroboros.Core
{
    /// <summary>
    /// Pure rules for the player profile (v0.13, milestone M5): what a match is worth, how experience turns into
    /// levels and titles, and how names and party codes are cleaned up. No Unity or Fusion types, so it is
    /// unit-tested outside the engine (Tools/tests).
    ///
    /// Progression is cosmetic by design: a level unlocks a title, never a stat. A new player and a veteran
    /// bring the same guns to the same fight.
    /// </summary>
    public static class ProfileRules
    {
        public const int MaxLevel = 50;
        public const int MaxNameLength = 16;
        public const int PartyCodeLength = 5;
        /// <summary>Letters and digits that cannot be mistaken for each other when read aloud or typed (no I, O, 0, 1).</summary>
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        // ------------------------------------------------------------------
        // Experience

        /// <summary>Experience for one finished match. Getting out with the loot is worth more than kills.</summary>
        public static int MatchXp(int lootBanked, bool extracted, bool won, int kills)
        {
            int xp = 50;                                   // turning up
            xp += Math.Max(0, lootBanked) / 10;            // 3000 banked = 300
            if (extracted) xp += 150;
            if (won) xp += 300;
            xp += Math.Max(0, kills) * 20;
            return Math.Min(xp, 2500);
        }

        /// <summary>Experience needed to go from <paramref name="level"/> to the next one.</summary>
        public static int XpForNextLevel(int level)
        {
            int l = Math.Max(1, Math.Min(level, MaxLevel));
            return 400 + 100 * l;
        }

        /// <summary>Total experience at which <paramref name="level"/> is reached. Level 1 is 0.</summary>
        public static int XpToReach(int level)
        {
            int l = Math.Max(1, Math.Min(level, MaxLevel));
            int n = l - 1;
            return 400 * n + 50 * n * (n + 1);
        }

        public static int LevelForXp(int xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= XpToReach(level + 1)) level++;
            return level;
        }

        /// <summary>0..1 through the current level. 1 at the level cap.</summary>
        public static float LevelProgress(int xp)
        {
            int level = LevelForXp(xp);
            if (level >= MaxLevel) return 1f;
            int start = XpToReach(level), end = XpToReach(level + 1);
            return end <= start ? 1f : Math.Max(0f, Math.Min(1f, (xp - start) / (float)(end - start)));
        }

        /// <summary>The title shown beside a player's level. Cosmetic.</summary>
        public static string TitleForLevel(int level)
        {
            if (level >= 40) return "Legend of the Freeport";
            if (level >= 30) return "Mastermind";
            if (level >= 22) return "Ghost";
            if (level >= 15) return "Safecracker";
            if (level >= 10) return "Fixer";
            if (level >= 6) return "Wheelman";
            if (level >= 3) return "Lookout";
            return "Runner";
        }

        /// <summary>The level at which the next title is earned, or 0 when there is none left.</summary>
        public static int NextTitleLevel(int level)
        {
            int[] steps = { 3, 6, 10, 15, 22, 30, 40 };
            for (int i = 0; i < steps.Length; i++) if (level < steps[i]) return steps[i];
            return 0;
        }

        // ------------------------------------------------------------------
        // Names and party codes

        /// <summary>
        /// A display name safe to show to other players: letters, digits, spaces, dashes and underscores, trimmed,
        /// at most <see cref="MaxNameLength"/> long. Anything else becomes <paramref name="fallback"/>.
        /// </summary>
        public static string SanitiseName(string raw, string fallback = "Runner")
        {
            if (string.IsNullOrEmpty(raw)) return fallback;
            var sb = new StringBuilder(MaxNameLength);
            bool lastSpace = true; // drops leading spaces and doubles
            foreach (char c in raw)
            {
                if (sb.Length >= MaxNameLength) break;
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (ok) { sb.Append(c); lastSpace = false; }
                else if (c == ' ' && !lastSpace) { sb.Append(' '); lastSpace = true; }
            }
            string name = sb.ToString().TrimEnd();
            return name.Length == 0 ? fallback : name;
        }

        /// <summary>A party code from a seed. The same seed always gives the same code.</summary>
        public static string PartyCode(int seed)
        {
            uint state = (uint)seed * 2654435761u + 0x9E3779B9u;
            var chars = new char[PartyCodeLength];
            for (int i = 0; i < PartyCodeLength; i++)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5; // xorshift
                chars[i] = CodeAlphabet[(int)(state % (uint)CodeAlphabet.Length)];
            }
            return new string(chars);
        }

        /// <summary>What a player typed, turned into a code: upper case, look-alikes mapped, everything else dropped.</summary>
        public static string NormaliseCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(PartyCodeLength);
            foreach (char typed in raw)
            {
                if (sb.Length >= PartyCodeLength) break;
                char c = char.ToUpperInvariant(typed);
                if (c == 'O' || c == '0') c = 'Q';       // not in the alphabet: nearest shape that is
                else if (c == 'I' || c == '1') c = 'L';
                if (CodeAlphabet.IndexOf(c) >= 0) sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsValidCode(string code)
        {
            return code != null && code.Length == PartyCodeLength && NormaliseCode(code) == code;
        }
    }
}
