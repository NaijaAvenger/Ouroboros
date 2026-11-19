using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// ScriptableObject for game mode configuration.
    /// Allows designers to create different game mode variations.
    ///
    /// v0.2: now actually consumed by <c>ExtractionHeistGameMode</c>, <c>ExtractionPoint</c>,
    /// <c>LootObjective</c> and <c>NetworkPlayer</c>. Fields marked (v0.2) are new.
    /// </summary>
    [CreateAssetMenu(fileName = "New Game Mode", menuName = "Ouroboros/Game Mode Config")]
    public class GameModeConfig : ScriptableObject
    {
        [Header("Mode Information")]
        public string modeName = "Extraction Heist";
        [TextArea(3, 5)]
        public string modeDescription;

        [Header("Match Configuration")]
        public float matchDuration = 900f; // seconds
        [Tooltip("(v0.2) Countdown between StartMatch and gameplay.")]
        // [v0.22] public float preMatchCountdown = 10f;
        public float preMatchCountdown = 30f; // (v0.23) the prep phase: choose a class and gear, read the briefing
        public int minPlayersToStart = 8;
        public int maxPlayers = 16;
        public bool allowLateJoin = true;
        [Tooltip("(v0.2) Start automatically once minPlayersToStart have joined.")]
        public bool autoStart = true;
        [Tooltip("(v0.2) Allow class changes from the lobby / class picker after spawn (pre-match only).")]
        public bool allowClassChangeBeforeMatch = true;
        [Tooltip("(v0.3) Allow abilities while waiting / counting down. Off by default so nobody pre-places traps.")]
        public bool allowAbilitiesBeforeMatch = false;

        [Header("Team Configuration")]
        public int numberOfTeams = 4;
        // [v0.9] public int playersPerTeam = 4;
        [Tooltip("(v0.10) Crew size. Three by default: easier to fill and to balance. The structural maximum is GameConstants.PLAYERS_PER_TEAM.")]
        public int playersPerTeam = 3;
        public bool allowTeamSwitching = false;
        [Tooltip("(v0.2) Whether teammates can damage each other.")]
        public bool allowFriendlyFire = false;

        [Header("Objectives")]
        public int objectivesToComplete = 3;
        public float objectiveRespawnTime = 60f;
        public bool requireAllObjectivesForExtraction = false;

        [Header("Extraction")]
        public int numberOfExtractionPoints = 4;
        public float extractionTime = 10f;
        public bool requireWholeTeamForExtraction = false;
        [Tooltip("Seconds into the match before extraction points open.")]
        public float extractionPointActivationDelay = 300f; // 5 minutes
        [Tooltip("(v0.2) Extra time after the match timer expires during which teams may still extract. Loot not extracted by then is lost.")]
        public float extractionWindowDuration = 120f;
        [Tooltip("(v0.2) Seconds an extraction point stays closed after a successful extraction (0 = single use).")]
        public float extractionReactivationDelay = 30f;
        [Tooltip("(v0.2) Enemies inside the zone pause the extraction timer.")]
        public bool extractionContestable = true;
        [Tooltip("(v0.2) Players must carry loot to be eligible to extract.")]
        public bool requireLootToExtract = false;

        [Header("Scoring")]
        public int pointsPerObjective = 100;
        public int pointsPerKill = 25;
        public int pointsPerExtraction = 500;
        public int pointsPerTeamMemberExtracted = 100;
        [Tooltip("(v0.2) Score awarded per unit of loot banked by extracting.")]
        public float pointsPerLoot = 1f;

        [Header("Alarm (v0.5)")]
        public float alarmDecayPerSecond = 1.5f;
        public float alarmSuspiciousThreshold = 25f;
        public float alarmAlertThreshold = 50f;
        public float alarmLockdownThreshold = 80f;
        [Tooltip("Seconds the alarm stops decaying after entering lockdown.")]
        public float lockdownHoldSeconds = 45f;
        [Tooltip("Extraction takes this many times longer during lockdown.")]
        public float lockdownExtractionTimeMultiplier = 1.5f;
        public float alarmOnObjectiveCompleted = 20f;
        public float alarmOnObjectiveStage = 10f;
        public float alarmOnBreach = 30f;
        public float alarmOnCameraSpotPerSecond = 8f;
        public float alarmOnGuardEngaged = 15f;
        public float alarmOnKill = 10f;
        [Tooltip("(v0.11) Alarm added by a wrong answer in a deep hack. Mistakes cost heat, not a lockout.")]
        public float alarmOnHackMistake = 6f;

        [Header("Carry Weight (v0.5)")]
        [Tooltip("Max movement slowdown fraction from carried loot (0.25 = 25% slower at the reference amount).")]
        public float lootWeightMaxSlowdown = 0.25f;
        [Tooltip("Carried loot amount at which the max slowdown applies.")]
        public int lootWeightReference = 1500;
        [Tooltip("Movement multiplier while carrying a LootCase.")]
        public float caseCarrySpeedMultiplier = 0.7f;
        [Tooltip("Score per unit of LootCase value banked (cases are worth more than vault loot).")]
        public float pointsPerCaseLoot = 1.5f;

        [Header("The Run (v0.10): quota race and getaway")]
        [Tooltip("Value a crew must be carrying to call the getaway for everyone. 0 = old rules (exits open on a timer, mixed scoring).")]
        public int lootQuota = 3000;
        [Tooltip("Fraction of carried value banked at a side exit. The central getaway always pays in full.")]
        [Range(0f, 1f)] public float sideExitPayout = 0.6f;
        [Tooltip("Score is the value a crew extracted, nothing else. Kills and cracked vaults matter only through loot.")]
        public bool scoreIsBankedLootOnly = true;

        [Header("Cash-out stations (v0.16)")]
        [Tooltip("Fraction of a station's pot that is banked when the cash-out finishes.")]
        [Range(0f, 1f)] public float cashOutPayout = 1f;
        [Tooltip("Money already banked counts toward the getaway quota, as well as what a crew is carrying.")]
        public bool quotaCountsBanked = true;
        [Tooltip("Named loot items one player can carry (the list holds 8 at most). Forces a crew to share the load.")]
        [Range(1, 8)] public int maxCarriedItems = 4;
        [Tooltip("During the getaway, players of the richest crew who carry loot are revealed to everyone.")]
        public bool revealRichestCrew = true;
        [Tooltip("Slam the facility into lockdown when the getaway is called.")]
        public bool lockdownOnGetaway = true;

        [Header("Crews and bots (v0.10)")]
        [Tooltip("How joining humans are placed. FillCrews keeps friends together against bot crews; Balance spreads them out for PvP.")]
        public HumanAssignment humanAssignment = HumanAssignment.FillCrews;
        [Tooltip("When the match starts, fill every empty crew seat with a bot (Host / Server mode only).")]
        public bool fillWithBots = true;
        [Tooltip("0 = slow to react and inaccurate, 1 = sharp. Affects reaction time, aim error and turn speed.")]
        [Range(0f, 1f)] public float botSkill = 0.5f;

        [Header("Crew wipes (v0.10)")]
        [Tooltip("A dead player only comes back while a crewmate is still alive. If the whole crew is down at once it is a wipe.")]
        public bool respawnRequiresLivingCrewmate = true;
        [Tooltip("Seconds before a wiped crew redeploys together (they have lost everything they carried).")]
        public float crewWipeRedeployDelay = 20f;
        [Tooltip("Wipes a crew can recover from. The next one eliminates it. -1 = unlimited.")]
        public int maxCrewWipes = 1;

        [Tooltip("Set by the scene builder when it has migrated an older config asset to the current rules.")]
        public int configVersion = 0;

        [Header("Respawn Settings")]
        public bool allowRespawning = true;
        public float respawnDelay = 10f;
        public int maxRespawns = 5;
        [Tooltip("(v0.2) Whether respawns are still allowed once the extraction phase starts.")]
        public bool respawnDuringExtractionPhase = false;
        [Tooltip("(v0.2) Drop carried loot as a pickup on death (otherwise it is lost).")]
        public bool dropLootOnDeath = true;

        [Header("Changing class and gear during a match (v0.23)")]
        [Tooltip("A player may change class and gear during the match while standing at their own crew's gate, and for a moment after a redeploy.")]
        public bool allowLoadoutChangeAtGate = true;
        [Tooltip("How close to one of the crew's spawn points counts as being at the gate, in metres.")]
        public float gateRadius = 12f;
        [Tooltip("Seconds after a redeploy during which class and gear can still be changed, wherever the player has got to.")]
        public float loadoutSecondsAfterRedeploy = 15f;

        [Header("Revive and regeneration (v0.17)")]
        [Tooltip("A crewmate can stand a downed player back up by holding Interact at their ID card.")]
        public bool allowRevive = true;
        [Tooltip("Seconds a crewmate must hold Interact at the card.")]
        public float reviveSeconds = 3.5f;
        [Tooltip("Share of full health a revived player stands up with.")]
        [Range(0.05f, 1f)] public float reviveHealthFraction = 0.4f;
        [Tooltip("Seconds without taking damage before health starts to come back.")]
        public float healthRegenDelay = 6f;
        [Tooltip("Health per second once it starts. 0 switches regeneration off.")]
        public float healthRegenPerSecond = 12f;
        [Tooltip("Regeneration stops at this share of full health. Below 1 leaves room for a medic.")]
        [Range(0.1f, 1f)] public float healthRegenCap = 1f;

        /// <summary>(v0.10) Crew size actually used, kept inside the structural limit.</summary>
        public int CrewSize => Mathf.Clamp(playersPerTeam, 1, Core.GameConstants.PLAYERS_PER_TEAM);
        /// <summary>(v0.10) Number of crews actually used, kept inside the structural limit.</summary>
        public int CrewCount => Mathf.Clamp(numberOfTeams, 1, Core.GameConstants.MAX_TEAMS);
        /// <summary>(v0.10) True when the quota race rules are on.</summary>
        public bool UseRunRules => lootQuota > 0;

        /// <summary>Runtime fallback used when no asset is assigned.</summary>
        public static GameModeConfig CreateDefault()
        {
            var cfg = CreateInstance<GameModeConfig>();
            cfg.name = "Default Extraction Heist";
            return cfg;
        }
    }

    /// <summary>(v0.10) How joining humans are distributed over crews.</summary>
    public enum HumanAssignment
    {
        /// <summary>Fill the first crew with room. Friends play together against bot crews (PvE first).</summary>
        FillCrews,
        /// <summary>Join the smallest crew. Spreads humans out for PvP.</summary>
        Balance
    }
}
