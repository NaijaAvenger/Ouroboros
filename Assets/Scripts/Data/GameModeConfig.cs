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
        public float preMatchCountdown = 10f;
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
        public int playersPerTeam = 4;
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

        [Header("Carry Weight (v0.5)")]
        [Tooltip("Max movement slowdown fraction from carried loot (0.25 = 25% slower at the reference amount).")]
        public float lootWeightMaxSlowdown = 0.25f;
        [Tooltip("Carried loot amount at which the max slowdown applies.")]
        public int lootWeightReference = 1500;
        [Tooltip("Movement multiplier while carrying a LootCase.")]
        public float caseCarrySpeedMultiplier = 0.7f;
        [Tooltip("Score per unit of LootCase value banked (cases are worth more than vault loot).")]
        public float pointsPerCaseLoot = 1.5f;

        [Header("Respawn Settings")]
        public bool allowRespawning = true;
        public float respawnDelay = 10f;
        public int maxRespawns = 5;
        [Tooltip("(v0.2) Whether respawns are still allowed once the extraction phase starts.")]
        public bool respawnDuringExtractionPhase = false;
        [Tooltip("(v0.2) Drop carried loot as a pickup on death (otherwise it is lost).")]
        public bool dropLootOnDeath = true;

        /// <summary>Runtime fallback used when no asset is assigned.</summary>
        public static GameModeConfig CreateDefault()
        {
            var cfg = CreateInstance<GameModeConfig>();
            cfg.name = "Default Extraction Heist";
            return cfg;
        }
    }
}
