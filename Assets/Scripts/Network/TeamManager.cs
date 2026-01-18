using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.Network
{
    /// <summary>
    /// Manages team assignments and player distribution for the 4v4v4v4 game mode.
    /// </summary>
    public class TeamManager : NetworkBehaviour
    {
        [Networked, Capacity(Core.GameConstants.MAX_PLAYERS)]
        public NetworkArray<Core.TeamID> PlayerTeams => default;
        
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamPlayerCounts => default;
        
        private Dictionary<PlayerRef, Core.TeamID> playerTeamMap = new Dictionary<PlayerRef, Core.TeamID>();
        
        /// <summary>
        /// Assigns a player to the team with the fewest members.
        /// </summary>
        public Core.TeamID AssignPlayerToTeam(PlayerRef player)
        {
            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning("[TeamManager] Only state authority can assign teams");
                return Core.TeamID.None;
            }
            
            // Find team with minimum players
            Core.TeamID selectedTeam = Core.TeamID.TeamAlpha;
            int minPlayers = TeamPlayerCounts[(int)Core.TeamID.TeamAlpha - 1];
            
            for (int i = 1; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                int teamIndex = i;
                int playerCount = TeamPlayerCounts[teamIndex];
                
                if (playerCount < minPlayers)
                {
                    minPlayers = playerCount;
                    selectedTeam = (Core.TeamID)(i + 1);
                }
            }
            
            // Check if team is full
            if (minPlayers >= Core.GameConstants.PLAYERS_PER_TEAM)
            {
                Debug.LogWarning($"[TeamManager] All teams are full!");
                return Core.TeamID.None;
            }
            
            // Assign player to team
            int teamIdx = (int)selectedTeam - 1;
            TeamPlayerCounts.Set(teamIdx, TeamPlayerCounts[teamIdx] + 1);
            playerTeamMap[player] = selectedTeam;
            
            Debug.Log($"[TeamManager] Assigned player {player} to {selectedTeam}");
            return selectedTeam;
        }
        
        /// <summary>
        /// Removes a player from their team.
        /// </summary>
        public void RemovePlayerFromTeam(PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;
            
            if (playerTeamMap.TryGetValue(player, out Core.TeamID team))
            {
                int teamIdx = (int)team - 1;
                TeamPlayerCounts.Set(teamIdx, Mathf.Max(0, TeamPlayerCounts[teamIdx] - 1));
                playerTeamMap.Remove(player);
                
                Debug.Log($"[TeamManager] Removed player {player} from {team}");
            }
        }
        
        /// <summary>
        /// Gets the team assignment for a specific player.
        /// </summary>
        public Core.TeamID GetPlayerTeam(PlayerRef player)
        {
            return playerTeamMap.TryGetValue(player, out Core.TeamID team) ? team : Core.TeamID.None;
        }
        
        /// <summary>
        /// Gets the number of players on a specific team.
        /// </summary>
        public int GetTeamPlayerCount(Core.TeamID team)
        {
            if (team == Core.TeamID.None) return 0;
            int teamIdx = (int)team - 1;
            return TeamPlayerCounts[teamIdx];
        }
    }
}
