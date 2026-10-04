using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.Network
{
    /// <summary>
    /// Manages team assignments and player distribution for the 4v4v4v4 game mode.
    ///
    /// v0.2: assignments live in a replicated <see cref="NetworkDictionary{K,V}"/> so every peer can
    /// resolve a player's team (the v0.1 dictionary only existed on the state authority).
    /// </summary>
    public class TeamManager : NetworkBehaviour
    {
        public static TeamManager Instance { get; private set; }

        // [v0.1] [Networked, Capacity(Core.GameConstants.MAX_PLAYERS)]
        // [v0.1] public NetworkArray<Core.TeamID> PlayerTeams => default;        // declared but never written
        // [v0.1] [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        // [v0.1] public NetworkArray<int> TeamPlayerCounts => default;           // now derived from PlayerTeams
        // [v0.1] private Dictionary<PlayerRef, Core.TeamID> playerTeamMap = new Dictionary<PlayerRef, Core.TeamID>();

        [Networked, Capacity(Core.GameConstants.MAX_PLAYERS)]
        public NetworkDictionary<PlayerRef, Core.TeamID> PlayerTeams => default;

        public override void Spawned()
        {
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Assigns a player to the team with the fewest members (or <paramref name="preferred"/> if it has room).
        /// </summary>
        public Core.TeamID AssignPlayerToTeam(PlayerRef player, Core.TeamID preferred = Core.TeamID.None)
        {
            if (!Object.HasStateAuthority)
            {
                Debug.LogWarning("[TeamManager] Only state authority can assign teams");
                return Core.TeamID.None;
            }

            if (PlayerTeams.TryGet(player, out Core.TeamID existing) && existing != Core.TeamID.None)
            {
                return existing;
            }

            int[] counts = new int[Core.GameConstants.MAX_TEAMS];
            CountTeams(counts);

            Core.TeamID selectedTeam = Core.TeamID.None;

            if (Core.TeamUtil.IsValid(preferred) && counts[Core.TeamUtil.ToIndex(preferred)] < Core.GameConstants.PLAYERS_PER_TEAM)
            {
                selectedTeam = preferred;
            }
            else
            {
                // Find team with minimum players
                int minPlayers = int.MaxValue;
                for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
                {
                    if (counts[i] < minPlayers)
                    {
                        minPlayers = counts[i];
                        selectedTeam = Core.TeamUtil.FromIndex(i);
                    }
                }

                // Check if team is full
                if (minPlayers >= Core.GameConstants.PLAYERS_PER_TEAM)
                {
                    Debug.LogWarning("[TeamManager] All teams are full!");
                    return Core.TeamID.None;
                }
            }

            PlayerTeams.Set(player, selectedTeam);
            Debug.Log($"[TeamManager] Assigned player {player} to {selectedTeam}");
            return selectedTeam;
        }

        /// <summary>
        /// Removes a player from their team.
        /// </summary>
        public void RemovePlayerFromTeam(PlayerRef player)
        {
            if (!Object.HasStateAuthority) return;

            if (PlayerTeams.TryGet(player, out Core.TeamID team))
            {
                PlayerTeams.Remove(player);
                Debug.Log($"[TeamManager] Removed player {player} from {team}");
            }
        }

        /// <summary>
        /// Gets the team assignment for a specific player. Works on every peer.
        /// </summary>
        public Core.TeamID GetPlayerTeam(PlayerRef player)
        {
            return PlayerTeams.TryGet(player, out Core.TeamID team) ? team : Core.TeamID.None;
        }

        /// <summary>
        /// Gets the number of players on a specific team.
        /// </summary>
        public int GetTeamPlayerCount(Core.TeamID team)
        {
            if (!Core.TeamUtil.IsValid(team)) return 0;
            int count = 0;
            foreach (var kvp in PlayerTeams)
            {
                if (kvp.Value == team) count++;
            }
            return count;
        }

        /// <summary>Fills <paramref name="buffer"/> with the PlayerRefs on <paramref name="team"/>.</summary>
        public void GetTeamMembers(Core.TeamID team, List<PlayerRef> buffer)
        {
            buffer.Clear();
            foreach (var kvp in PlayerTeams)
            {
                if (kvp.Value == team) buffer.Add(kvp.Key);
            }
        }

        /// <summary>Number of teams that currently have at least one player.</summary>
        public int ActiveTeamCount()
        {
            int[] counts = new int[Core.GameConstants.MAX_TEAMS];
            CountTeams(counts);
            int active = 0;
            for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) active++;
            return active;
        }

        /// <summary>
        /// Match-state snapshot for a team, derived from the live <see cref="NetworkPlayer"/> registry.
        /// </summary>
        public TeamStatus GetTeamStatus(Core.TeamID team)
        {
            var status = new TeamStatus();
            for (int i = 0; i < NetworkPlayer.All.Count; i++)
            {
                var p = NetworkPlayer.All[i];
                if (p == null || p.Object == null || p.Team != team) continue;
                status.Total++;
                if (p.IsExtracted) status.Extracted++;
                else if (p.IsAlive) status.Alive++;
                else if (p.RespawnTimer.IsRunning) status.AwaitingRespawn++;
                else status.Dead++;
                status.CarriedLoot += p.CarriedLoot;
            }
            return status;
        }

        /// <summary>True when no member of the team can still act (everyone extracted or permanently dead).</summary>
        public bool IsTeamFinished(Core.TeamID team)
        {
            var s = GetTeamStatus(team);
            return s.Total > 0 && s.Alive == 0 && s.AwaitingRespawn == 0;
        }

        private void CountTeams(int[] counts)
        {
            foreach (var kvp in PlayerTeams)
            {
                if (Core.TeamUtil.IsValid(kvp.Value))
                {
                    counts[Core.TeamUtil.ToIndex(kvp.Value)]++;
                }
            }
        }

        public struct TeamStatus
        {
            public int Total;
            public int Alive;
            public int AwaitingRespawn;
            public int Dead;
            public int Extracted;
            public int CarriedLoot;
        }
    }
}
