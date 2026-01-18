using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// ScriptableObject for game mode configuration.
    /// Allows designers to create different game mode variations.
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
        public int minPlayersToStart = 8;
        public int maxPlayers = 16;
        public bool allowLateJoin = true;
        
        [Header("Team Configuration")]
        public int numberOfTeams = 4;
        public int playersPerTeam = 4;
        public bool allowTeamSwitching = false;
        
        [Header("Objectives")]
        public int objectivesToComplete = 3;
        public float objectiveRespawnTime = 60f;
        public bool requireAllObjectivesForExtraction = false;
        
        [Header("Extraction")]
        public int numberOfExtractionPoints = 4;
        public float extractionTime = 10f;
        public bool requireWholeTeamForExtraction = false;
        public float extractionPointActivationDelay = 300f; // 5 minutes
        
        [Header("Scoring")]
        public int pointsPerObjective = 100;
        public int pointsPerKill = 25;
        public int pointsPerExtraction = 500;
        public int pointsPerTeamMemberExtracted = 100;
        
        [Header("Respawn Settings")]
        public bool allowRespawning = true;
        public float respawnDelay = 10f;
        public int maxRespawns = 5;
    }
}
