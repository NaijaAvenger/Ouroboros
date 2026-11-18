using System;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// The local player's profile (v0.13, milestone M5): a name, experience and career totals, kept in
    /// PlayerPrefs on this machine. The arithmetic lives in <see cref="ProfileRules"/>.
    ///
    /// This is a local record. It is not validated by a server, so it must never gate anything that matters in
    /// a match: levels and titles are cosmetic only.
    /// </summary>
    public static class PlayerProfile
    {
        private const string Prefix = "ouroboros.profile.";
        private static bool loaded;
        private static string name = "Runner";
        private static int xp, matches, wins, extractions, kills, lootBanked, bestHaul;

        /// <summary>Raised after the name or any total changes.</summary>
        public static event Action Changed;

        /// <summary>What the last recorded match added, for the summary screen.</summary>
        public struct MatchRecord
        {
            public bool Valid, Won, Extracted, LevelledUp;
            public int Xp, LootBanked, Kills, LevelBefore, LevelAfter;
        }
        public static MatchRecord LastMatch { get; private set; }

        public static string Name
        {
            get { Load(); return name; }
            set
            {
                Load();
                string clean = ProfileRules.SanitiseName(value);
                if (clean == name) return;
                name = clean;
                PlayerPrefs.SetString(Prefix + "name", name);
                Changed?.Invoke();
            }
        }

        public static int Xp { get { Load(); return xp; } }
        public static int Level => ProfileRules.LevelForXp(Xp);
        public static string Title => ProfileRules.TitleForLevel(Level);
        public static float LevelProgress => ProfileRules.LevelProgress(Xp);
        public static int Matches { get { Load(); return matches; } }
        public static int Wins { get { Load(); return wins; } }
        public static int Extractions { get { Load(); return extractions; } }
        public static int Kills { get { Load(); return kills; } }
        public static int LootBanked { get { Load(); return lootBanked; } }
        public static int BestHaul { get { Load(); return bestHaul; } }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            name = ProfileRules.SanitiseName(PlayerPrefs.GetString(Prefix + "name", "Runner"));
            xp = Mathf.Max(0, PlayerPrefs.GetInt(Prefix + "xp", 0));
            matches = PlayerPrefs.GetInt(Prefix + "matches", 0);
            wins = PlayerPrefs.GetInt(Prefix + "wins", 0);
            extractions = PlayerPrefs.GetInt(Prefix + "extractions", 0);
            kills = PlayerPrefs.GetInt(Prefix + "kills", 0);
            lootBanked = PlayerPrefs.GetInt(Prefix + "lootBanked", 0);
            bestHaul = PlayerPrefs.GetInt(Prefix + "bestHaul", 0);
        }

        /// <summary>Adds one finished match to the profile and returns what it was worth.</summary>
        public static MatchRecord RecordMatch(int crewLootBanked, bool extracted, bool won, int matchKills)
        {
            Load();
            int before = Level;
            int gained = ProfileRules.MatchXp(crewLootBanked, extracted, won, matchKills);

            xp += gained;
            matches++;
            if (won) wins++;
            if (extracted) extractions++;
            kills += Mathf.Max(0, matchKills);
            lootBanked += Mathf.Max(0, crewLootBanked);
            bestHaul = Mathf.Max(bestHaul, crewLootBanked);

            PlayerPrefs.SetInt(Prefix + "xp", xp);
            PlayerPrefs.SetInt(Prefix + "matches", matches);
            PlayerPrefs.SetInt(Prefix + "wins", wins);
            PlayerPrefs.SetInt(Prefix + "extractions", extractions);
            PlayerPrefs.SetInt(Prefix + "kills", kills);
            PlayerPrefs.SetInt(Prefix + "lootBanked", lootBanked);
            PlayerPrefs.SetInt(Prefix + "bestHaul", bestHaul);
            PlayerPrefs.Save();

            int after = Level;
            LastMatch = new MatchRecord
            {
                Valid = true, Won = won, Extracted = extracted, Xp = gained, LootBanked = crewLootBanked, Kills = matchKills,
                LevelBefore = before, LevelAfter = after, LevelledUp = after > before
            };
            Changed?.Invoke();
            return LastMatch;
        }

        /// <summary>Wipes the career totals. The name is kept.</summary>
        public static void ResetProgress()
        {
            Load();
            xp = matches = wins = extractions = kills = lootBanked = bestHaul = 0;
            foreach (string key in new[] { "xp", "matches", "wins", "extractions", "kills", "lootBanked", "bestHaul" }) PlayerPrefs.DeleteKey(Prefix + key);
            LastMatch = default;
            Changed?.Invoke();
        }
    }
}
