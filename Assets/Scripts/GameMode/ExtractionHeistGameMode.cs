using System;
using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Core game mode manager for the 4v4v4v4 extraction heist.
    /// Handles match flow, objectives, scoring, loot banking and extraction mechanics.
    ///
    /// v0.2 flow:
    ///   WaitingForPlayers → (StartMatch / auto-start) → PreMatch countdown → InProgress
    ///   → extraction points open after <c>extractionPointActivationDelay</c>
    ///   → match timer expires → Extraction window → MatchEnded (or earlier when every team is finished)
    /// </summary>
    public class ExtractionHeistGameMode : NetworkBehaviour
    {
        public static ExtractionHeistGameMode Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private Data.GameModeConfig config;
        // [v0.1] [SerializeField] private float matchDuration = 900f;  // now GameModeConfig.matchDuration
        // [v0.1] [SerializeField] private int objectivesRequired = 3;  // now GameModeConfig.objectivesToComplete

        [Header("Prefabs (optional)")]
        [Tooltip("Spawned where a player dies carrying loot. Leave empty to simply lose the loot.")]
        [SerializeField] private NetworkPrefabRef lootDropPrefab;
        [Tooltip("Fallback direct prefab reference (filled by the Phase 0 scene builder).")]
        [SerializeField] private NetworkObject lootDropPrefabObject;

        [Networked] public TickTimer MatchTimer { get; set; }
        /// <summary>Timer for the current phase (pre-match countdown, extraction window).</summary>
        [Networked] public TickTimer PhaseTimer { get; set; }
        [Networked] public GameState CurrentState { get; set; }
        [Networked] public NetworkBool ExtractionOpen { get; set; }
        [Networked] public Core.TeamID WinningTeam { get; set; }
        [Networked] public int MatchStartTick { get; set; }

        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamScores => default;
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<NetworkBool> TeamExtracted => default;
        /// <summary>Loot banked by each team through successful extractions (v0.2).</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamLoot => default;
        /// <summary>Objectives completed per team (v0.2).</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamObjectives => default;
        /// <summary>Members extracted per team (v0.2).</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamExtractedCount => default;

        // [v0.1] private List<ExtractionPoint> extractionPoints = new List<ExtractionPoint>(); // never populated
        private readonly List<ExtractionPoint> extractionPoints = new List<ExtractionPoint>();
        private ChangeDetector changeDetector;
        private Data.GameModeConfig runtimeConfig;

        /// <summary>Fired on every peer when <see cref="CurrentState"/> changes.</summary>
        public static event Action<GameState> StateChanged;

        public enum GameState
        {
            WaitingForPlayers,
            PreMatch,
            InProgress,
            Extraction,
            MatchEnded
        }

        public Data.GameModeConfig Config
        {
            get
            {
                if (config != null) return config;
                if (runtimeConfig == null) runtimeConfig = Data.GameModeConfig.CreateDefault();
                return runtimeConfig;
            }
        }

        public bool IsMatchLive => CurrentState == GameState.InProgress || CurrentState == GameState.Extraction;
        public bool AllowClassChange => Config.allowClassChangeBeforeMatch && (CurrentState == GameState.WaitingForPlayers || CurrentState == GameState.PreMatch);
        public float? MatchTimeRemaining => MatchTimer.RemainingTime(Runner);
        public float? PhaseTimeRemaining => PhaseTimer.RemainingTime(Runner);
        public float MatchElapsedSeconds => Runner != null && MatchStartTick > 0 ? ((int)Runner.Tick - MatchStartTick) * Runner.DeltaTime : 0f;
        public IReadOnlyList<ExtractionPoint> ExtractionPoints => extractionPoints;

        public override void Spawned()
        {
            Instance = this;
            changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            if (Object.HasStateAuthority)
            {
                CurrentState = GameState.WaitingForPlayers;
                ExtractionOpen = false;
                WinningTeam = Core.TeamID.None;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        public override void Render()
        {
            if (changeDetector == null) return;
            foreach (var change in changeDetector.DetectChanges(this))
            {
                if (change == nameof(CurrentState)) StateChanged?.Invoke(CurrentState);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            switch (CurrentState)
            {
                case GameState.PreMatch:
                    if (PhaseTimer.Expired(Runner)) BeginMatch();
                    break;

                case GameState.InProgress:
                    UpdateMatchTimer();
                    if (!ExtractionOpen && MatchElapsedSeconds >= Config.extractionPointActivationDelay)
                    {
                        OpenExtraction();
                    }
                    CheckEarlyEnd();
                    break;

                case GameState.Extraction:
                    if (PhaseTimer.Expired(Runner))
                    {
                        EndMatch();
                    }
                    else
                    {
                        CheckEarlyEnd();
                    }
                    break;
            }
        }

        private void UpdateMatchTimer()
        {
            if (MatchTimer.Expired(Runner))
            {
                EnterExtractionPhase();
            }
        }

        // ------------------------------------------------------------------
        // Match flow
        // ------------------------------------------------------------------

        /// <summary>Called by the session manager whenever the player count changes (state authority only).</summary>
        public void NotifyPlayerCountChanged(int playerCount)
        {
            if (!Object.HasStateAuthority) return;
            if (CurrentState == GameState.WaitingForPlayers && Config.autoStart && playerCount >= Mathf.Max(1, Config.minPlayersToStart))
            {
                StartMatch();
            }
        }

        /// <summary>
        /// Starts the pre-match countdown. Safe to call from a lobby button; no-op unless waiting for players.
        /// </summary>
        public void StartMatch()
        {
            if (!Object.HasStateAuthority || CurrentState != GameState.WaitingForPlayers) return;

            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                TeamScores.Set(i, 0);
                TeamLoot.Set(i, 0);
                TeamObjectives.Set(i, 0);
                TeamExtractedCount.Set(i, 0);
                TeamExtracted.Set(i, false);
            }

            if (Config.preMatchCountdown > 0f)
            {
                CurrentState = GameState.PreMatch;
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, Config.preMatchCountdown);
                RPC_OnPreMatch(Config.preMatchCountdown);
            }
            else
            {
                BeginMatch();
            }
        }

        private void BeginMatch()
        {
            MatchTimer = TickTimer.CreateFromSeconds(Runner, Config.matchDuration);
            MatchStartTick = (int)Runner.Tick;
            PhaseTimer = TickTimer.None;
            CurrentState = GameState.InProgress;

            if (Config.extractionPointActivationDelay <= 0f) OpenExtraction();

            RPC_OnMatchStart();
        }

        private void OpenExtraction()
        {
            ExtractionOpen = true;
            RPC_OnExtractionOpened();
        }

        private void EnterExtractionPhase()
        {
            CurrentState = GameState.Extraction;
            if (!ExtractionOpen) OpenExtraction();
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, Config.extractionWindowDuration);
            RPC_AnnounceExtractionPhase(Config.extractionWindowDuration);
        }

        /// <summary>Ends early once every team with players has either extracted or been wiped.</summary>
        private void CheckEarlyEnd()
        {
            var tm = Network.TeamManager.Instance;
            if (tm == null) return;

            int activeTeams = 0;
            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                Core.TeamID team = Core.TeamUtil.FromIndex(i);
                if (tm.GetTeamPlayerCount(team) == 0) continue;
                activeTeams++;
                if (!tm.IsTeamFinished(team)) return;
            }

            if (activeTeams > 0) EndMatch();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnPreMatch(float countdown)
        {
            Debug.Log($"[ExtractionHeist] Match starting in {countdown:0}s");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnMatchStart()
        {
            Debug.Log("[ExtractionHeist] Match started!");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnExtractionOpened()
        {
            Debug.Log("[ExtractionHeist] Extraction points are now open");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AnnounceExtractionPhase(float window)
        {
            Debug.Log($"[ExtractionHeist] Extraction phase begun! {window:0}s to get out!");
        }

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        /// <summary>
        /// Awards points to a team. Virtual so game-mode variants can scale or veto points.
        /// </summary>
        public virtual void AwardTeamScore(Core.TeamID team, int points)
        {
            if (!Object.HasStateAuthority || points == 0) return;

            int teamIdx = Core.TeamUtil.ToIndex(team);
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

        public int GetTeamScore(Core.TeamID team)
        {
            int idx = Core.TeamUtil.ToIndex(team);
            return idx >= 0 && idx < Core.GameConstants.MAX_TEAMS ? TeamScores[idx] : 0;
        }

        /// <summary>Called by <see cref="LootObjective"/> when a team finishes an objective.</summary>
        public void OnObjectiveCompleted(Core.TeamID team, LootObjective objective)
        {
            if (!Object.HasStateAuthority || !Core.TeamUtil.IsValid(team)) return;
            int idx = Core.TeamUtil.ToIndex(team);
            TeamObjectives.Set(idx, TeamObjectives[idx] + 1);
            AwardTeamScore(team, Config.pointsPerObjective);
        }

        /// <summary>Kill credit and loot drop. Called by <see cref="Network.NetworkPlayer"/> on death.</summary>
        public void OnPlayerDied(Network.NetworkPlayer victim, Network.NetworkPlayer killer)
        {
            if (!Object.HasStateAuthority || victim == null) return;

            if (killer != null && killer != victim && killer.Team != victim.Team)
            {
                AwardTeamScore(killer.Team, Config.pointsPerKill);
            }

            int loot = victim.TakeAllLoot();
            if (loot > 0 && Config.dropLootOnDeath && (lootDropPrefab.IsValid || lootDropPrefabObject != null))
            {
                Vector3 pos = victim.transform.position + Vector3.up * 0.5f;
                void InitDrop(NetworkRunner runner, NetworkObject obj)
                {
                    var drop = obj.GetComponent<LootDrop>();
                    if (drop != null) drop.Value = loot;
                }
                if (lootDropPrefab.IsValid) Runner.Spawn(lootDropPrefab, pos, Quaternion.identity, null, InitDrop);
                else Runner.Spawn(lootDropPrefabObject, pos, Quaternion.identity, null, InitDrop);
            }
        }

        /// <summary>Whether a dead player is allowed another respawn under the current rules.</summary>
        public bool CanRespawn(Network.NetworkPlayer player)
        {
            if (player == null || player.IsExtracted) return false;
            if (!Config.allowRespawning) return false;
            if (CurrentState == GameState.MatchEnded) return false;
            if (CurrentState == GameState.Extraction && !Config.respawnDuringExtractionPhase) return false;
            if (Config.maxRespawns >= 0 && player.RespawnsUsed >= Config.maxRespawns) return false;
            return true;
        }

        /// <summary>Whether a player currently meets the extraction eligibility rules.</summary>
        public bool CanExtract(Network.NetworkPlayer player)
        {
            if (player == null || !player.IsActiveInMatch) return false;
            if (!ExtractionOpen) return false;
            if (Config.requireLootToExtract && player.CarriedLoot <= 0) return false;
            if (Config.requireAllObjectivesForExtraction)
            {
                int idx = Core.TeamUtil.ToIndex(player.Team);
                if (idx < 0 || TeamObjectives[idx] < Config.objectivesToComplete) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Extraction
        // ------------------------------------------------------------------

        public void RegisterExtractionPoint(ExtractionPoint point)
        {
            if (point != null && !extractionPoints.Contains(point)) extractionPoints.Add(point);
        }

        public void UnregisterExtractionPoint(ExtractionPoint point)
        {
            extractionPoints.Remove(point);
        }

        /// <summary>
        /// Called by an <see cref="ExtractionPoint"/> when its timer completes with <paramref name="members"/>
        /// of <paramref name="team"/> inside. Banks their loot, scores, and removes them from play.
        /// </summary>
        public void OnTeamExtractionCompleted(Core.TeamID team, List<Network.NetworkPlayer> members)
        {
            if (!Object.HasStateAuthority || !Core.TeamUtil.IsValid(team)) return;

            int idx = Core.TeamUtil.ToIndex(team);
            int bankedLoot = 0;
            int extractedNow = 0;

            foreach (var member in members)
            {
                if (member == null || !CanExtract(member)) continue;
                bankedLoot += member.TakeAllLoot();
                member.MarkExtracted();
                extractedNow++;
            }

            if (extractedNow == 0) return;

            TeamLoot.Set(idx, TeamLoot[idx] + bankedLoot);
            TeamExtractedCount.Set(idx, TeamExtractedCount[idx] + extractedNow);

            int points = extractedNow * Config.pointsPerTeamMemberExtracted + Mathf.RoundToInt(bankedLoot * Config.pointsPerLoot);
            if (!TeamExtracted[idx])
            {
                // First successful extraction for this team
                TeamExtracted.Set(idx, true);
                points += Config.pointsPerExtraction;
            }
            AwardTeamScore(team, points);

            RPC_TeamExtracted(team, extractedNow, bankedLoot);
            // [v0.1] CheckMatchEnd();  // BUG: ended the match on the first extraction during the Extraction phase
            CheckEarlyEnd();
        }

        /// <summary>
        /// Legacy entry point: marks a team as extracted without member bookkeeping. Prefer
        /// <see cref="OnTeamExtractionCompleted"/>.
        /// </summary>
        public void TeamExtractedSuccessfully(Core.TeamID team)
        {
            if (!Object.HasStateAuthority) return;

            int teamIdx = Core.TeamUtil.ToIndex(team);
            if (teamIdx >= 0 && teamIdx < Core.GameConstants.MAX_TEAMS)
            {
                if (!TeamExtracted[teamIdx])
                {
                    TeamExtracted.Set(teamIdx, true);
                    AwardTeamScore(team, Config.pointsPerExtraction);
                }
                RPC_TeamExtracted(team, 0, 0);
                CheckEarlyEnd();
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TeamExtracted(Core.TeamID team, int members, int loot)
        {
            Debug.Log($"[ExtractionHeist] {team} extracted {members} member(s) with {loot} loot!");
        }

        // [v0.1] private void CheckMatchEnd()
        // [v0.1] {
        // [v0.1]     int extractedCount = 0;
        // [v0.1]     for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++) if (TeamExtracted[i]) extractedCount++;
        // [v0.1]     if (extractedCount >= Core.GameConstants.MAX_TEAMS || CurrentState == GameState.Extraction)
        // [v0.1]         EndMatch();   // <- any single extraction in the Extraction phase ended the match
        // [v0.1] }

        private void EndMatch()
        {
            if (CurrentState == GameState.MatchEnded) return;

            // Loot still carried when the match ends is lost: only banked loot counts.
            WinningTeam = DetermineWinner();
            CurrentState = GameState.MatchEnded;
            PhaseTimer = TickTimer.None;
            RPC_OnMatchEnd(WinningTeam);
        }

        /// <summary>Highest score wins; ties go to the team with more banked loot, then more extracted members.</summary>
        protected virtual Core.TeamID DetermineWinner()
        {
            Core.TeamID best = Core.TeamID.None;
            int bestScore = int.MinValue, bestLoot = int.MinValue, bestExtracted = int.MinValue;

            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                var tm = Network.TeamManager.Instance;
                if (tm != null && tm.GetTeamPlayerCount(Core.TeamUtil.FromIndex(i)) == 0) continue;

                int score = TeamScores[i], loot = TeamLoot[i], extracted = TeamExtractedCount[i];
                bool better = score > bestScore
                              || (score == bestScore && loot > bestLoot)
                              || (score == bestScore && loot == bestLoot && extracted > bestExtracted);
                if (better)
                {
                    best = Core.TeamUtil.FromIndex(i);
                    bestScore = score; bestLoot = loot; bestExtracted = extracted;
                }
            }
            return best;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnMatchEnd(Core.TeamID winner)
        {
            Debug.Log($"[ExtractionHeist] Match ended! Winner: {winner}");
            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                Debug.Log($"[ExtractionHeist]   {Core.TeamUtil.FromIndex(i)}: {TeamScores[i]} pts, {TeamLoot[i]} loot, {TeamExtractedCount[i]} extracted, {TeamObjectives[i]} objectives");
            }
        }
    }
}
