using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Represents an extraction point where teams can successfully extract.
    /// </summary>
    public class ExtractionPoint : NetworkBehaviour
    {
        [Header("Extraction Configuration")]
        [SerializeField] private float extractionTime = 10f;
        [SerializeField] private float extractionRadius = 5f;
        [SerializeField] private bool requiresAllTeamMembers = false;
        
        [Networked] public NetworkBool IsActive { get; set; }
        [Networked] public Core.TeamID CurrentExtractingTeam { get; set; }
        [Networked] public TickTimer ExtractionTimer { get; set; }
        
        private HashSet<PlayerRef> playersInZone = new HashSet<PlayerRef>();
        
        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                IsActive = true;
                CurrentExtractingTeam = Core.TeamID.None;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !IsActive) return;
            
            if (CurrentExtractingTeam != Core.TeamID.None)
            {
                if (ExtractionTimer.Expired(Runner))
                {
                    CompleteExtraction();
                }
            }
        }
        
        private void OnTriggerEnter(Collider other)
        {
            if (!Object.HasStateAuthority) return;
            
            Network.NetworkPlayer player = other.GetComponent<Network.NetworkPlayer>();
            if (player != null && player.IsAlive)
            {
                playersInZone.Add(player.PlayerRef);
                CheckExtractionStart(player.Team);
            }
        }
        
        private void OnTriggerExit(Collider other)
        {
            if (!Object.HasStateAuthority) return;
            
            Network.NetworkPlayer player = other.GetComponent<Network.NetworkPlayer>();
            if (player != null)
            {
                playersInZone.Remove(player.PlayerRef);
                
                if (player.Team == CurrentExtractingTeam)
                {
                    CheckExtractionCancellation();
                }
            }
        }
        
        private void CheckExtractionStart(Core.TeamID team)
        {
            if (CurrentExtractingTeam != Core.TeamID.None) return;
            
            // Count team members in zone
            int teamMembersInZone = 0;
            foreach (var playerRef in playersInZone)
            {
                // Would need to get player's team here
                teamMembersInZone++;
            }
            
            if (!requiresAllTeamMembers || teamMembersInZone >= Core.GameConstants.PLAYERS_PER_TEAM)
            {
                StartExtraction(team);
            }
        }
        
        private void StartExtraction(Core.TeamID team)
        {
            CurrentExtractingTeam = team;
            ExtractionTimer = TickTimer.CreateFromSeconds(Runner, extractionTime);
            RPC_ExtractionStarted(team);
        }
        
        private void CheckExtractionCancellation()
        {
            if (playersInZone.Count == 0 && CurrentExtractingTeam != Core.TeamID.None)
            {
                CancelExtraction();
            }
        }
        
        private void CancelExtraction()
        {
            Core.TeamID previousTeam = CurrentExtractingTeam;
            CurrentExtractingTeam = Core.TeamID.None;
            RPC_ExtractionCancelled(previousTeam);
        }
        
        private void CompleteExtraction()
        {
            Core.TeamID extractedTeam = CurrentExtractingTeam;
            IsActive = false;
            
            // Notify game mode
            var gameMode = FindObjectOfType<ExtractionHeistGameMode>();
            if (gameMode != null)
            {
                gameMode.TeamExtractedSuccessfully(extractedTeam);
            }
            
            RPC_ExtractionCompleted(extractedTeam);
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionStarted(Core.TeamID team)
        {
            Debug.Log($"[ExtractionPoint] {team} started extraction - {extractionTime}s remaining");
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionCancelled(Core.TeamID team)
        {
            Debug.Log($"[ExtractionPoint] {team} extraction cancelled");
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionCompleted(Core.TeamID team)
        {
            Debug.Log($"[ExtractionPoint] {team} successfully extracted!");
        }
    }
}
