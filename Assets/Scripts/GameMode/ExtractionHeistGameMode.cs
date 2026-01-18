using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Core game mode manager for the 4v4v4v4 extraction heist.
    /// Handles match flow, objectives, and extraction mechanics.
    /// </summary>
    public class ExtractionHeistGameMode : NetworkBehaviour
    {
        [Header("Match Configuration")]
        [SerializeField] private float matchDuration = 900f; // 15 minutes
        [SerializeField] private int objectivesRequired = 3;
        
        [Networked] public TickTimer MatchTimer { get; set; }
        [Networked] public GameState CurrentState { get; set; }
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamScores => default;
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<NetworkBool> TeamExtracted => default;
        
        private List<ExtractionPoint> extractionPoints = new List<ExtractionPoint>();
        
        public enum GameState
        {
            WaitingForPlayers,
            PreMatch,
            InProgress,
            Extraction,
            MatchEnded
        }
        
        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                CurrentState = GameState.WaitingForPlayers;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            
            switch (CurrentState)
            {
                case GameState.InProgress:
                    UpdateMatchTimer();
                    break;
            }
        }
        
        private void UpdateMatchTimer()
        {
            if (MatchTimer.Expired(Runner))
            {
                CurrentState = GameState.Extraction;
                RPC_AnnounceExtractionPhase();
            }
        }
        
        /// <summary>
        /// Starts the match when enough players have joined.
        /// </summary>
        public void StartMatch()
        {
            if (Object.HasStateAuthority && CurrentState == GameState.WaitingForPlayers)
            {
                MatchTimer = TickTimer.CreateFromSeconds(Runner, matchDuration);
                CurrentState = GameState.InProgress;
                RPC_OnMatchStart();
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnMatchStart()
        {
            Debug.Log("[ExtractionHeist] Match started!");
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AnnounceExtractionPhase()
        {
            Debug.Log("[ExtractionHeist] Extraction phase begun! Get to extraction points!");
        }
        
        /// <summary>
        /// Awards points to a team for completing objectives.
        /// </summary>
        public void AwardTeamScore(Core.TeamID team, int points)
        {
            if (!Object.HasStateAuthority) return;
            
            int teamIdx = (int)team - 1;
            if (teamIdx >= 0 && teamIdx < Core.GameConstants.MAX_TEAMS)
            {
                TeamScores.Set(teamIdx, TeamScores[teamIdx] + points);
                RPC_TeamScored(team, points);
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TeamScored(Core.TeamID team, int points)
        {
            Debug.Log($"[ExtractionHeist] {team} scored {points} points!");
        }
        
        /// <summary>
        /// Marks a team as successfully extracted.
        /// </summary>
        public void TeamExtractedSuccessfully(Core.TeamID team)
        {
            if (!Object.HasStateAuthority) return;
            
            int teamIdx = (int)team - 1;
            if (teamIdx >= 0 && teamIdx < Core.GameConstants.MAX_TEAMS)
            {
                TeamExtracted.Set(teamIdx, true);
                RPC_TeamExtracted(team);
                CheckMatchEnd();
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TeamExtracted(Core.TeamID team)
        {
            Debug.Log($"[ExtractionHeist] {team} successfully extracted!");
        }
        
        private void CheckMatchEnd()
        {
            // Check if all teams have extracted or timer expired
            int extractedCount = 0;
            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                if (TeamExtracted[i]) extractedCount++;
            }
            
            if (extractedCount >= Core.GameConstants.MAX_TEAMS || CurrentState == GameState.Extraction)
            {
                EndMatch();
            }
        }
        
        private void EndMatch()
        {
            CurrentState = GameState.MatchEnded;
            RPC_OnMatchEnd();
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnMatchEnd()
        {
            Debug.Log("[ExtractionHeist] Match ended! Calculating winners...");
            // Display final scores and determine winner
        }
    }
}
