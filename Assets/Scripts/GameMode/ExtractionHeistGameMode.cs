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
        [Tooltip("(v0.7) Dead players drop their ID card; other teams swipe it at PlayerCard vault doors.")]
        [SerializeField] private NetworkObject idCardPrefabObject;

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

        /// <summary>(v0.10) Crew whose haul called the getaway, or None when the match timer did.</summary>
        [Networked] public Core.TeamID TailTeam { get; set; }
        /// <summary>(v0.10) Crew carrying the most right now during the getaway: the one everybody is hunting.</summary>
        [Networked] public Core.TeamID RichestTeam { get; set; }
        /// <summary>(v0.10) Times each crew has been wiped (all members down at once).</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> CrewWipes => default;
        /// <summary>(v0.10) Crews with no redeploys left.</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<NetworkBool> CrewEliminated => default;

        /// <summary>(v0.10) Fired on every peer when the getaway is called (crew that hit the quota, or None for the timer).</summary>
        public static event Action<Core.TeamID> GetawayCalled;
        /// <summary>(v0.10) Fired on every peer when a whole crew goes down (crew, true if it is out for good).</summary>
        public static event Action<Core.TeamID, bool> CrewWiped;

        // [v0.1] private List<ExtractionPoint> extractionPoints = new List<ExtractionPoint>(); // never populated
        private readonly List<ExtractionPoint> extractionPoints = new List<ExtractionPoint>();
        private ChangeDetector changeDetector;
        private Data.GameModeConfig runtimeConfig;

        /// <summary>Fired on every peer when <see cref="CurrentState"/> changes.</summary>
        public static event Action<GameState> StateChanged;
        /// <summary>Fired on every peer when extraction points open (v0.3).</summary>
        public static event Action ExtractionOpened;
        /// <summary>Fired on every peer when a team extracts members (team, members, loot) (v0.3).</summary>
        public static event Action<Core.TeamID, int, int> TeamExtractedEvent;

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
        /// <summary>(v0.10) The getaway phase: exits are open, no more respawns, the richest crew is marked.</summary>
        public bool IsGetaway => CurrentState == GameState.Extraction;
        /// <summary>(v0.10) True when loot pickups can be spawned (a prefab is assigned).</summary>
        [Tooltip("(v0.14) Getaway quota for this map. 0 = use the config's. A bigger map with more loot wants a bigger quota.")]
        [SerializeField] private int lootQuotaOverride = 0;

        /// <summary>(v0.14) What a crew must be carrying for the getaway to be called, on this map.</summary>
        public int LootQuota => lootQuotaOverride > 0 ? lootQuotaOverride : Config.lootQuota;

        public bool CanSpawnLoot => lootDropPrefab.IsValid || lootDropPrefabObject != null;
        public bool AllowClassChange => Config.allowClassChangeBeforeMatch && (CurrentState == GameState.WaitingForPlayers || CurrentState == GameState.PreMatch);
        /// <summary>
        /// (v0.23) Whether <paramref name="player"/> may change class and gear right now: always before the
        /// match, and during it while they stand at their own crew's gate or have just redeployed. Works on
        /// any peer; the state authority is the one that enforces it.
        /// </summary>
        public bool CanChangeLoadout(Network.NetworkPlayer player)
        {
            if (player == null || player.Object == null) return false;
            if (AllowClassChange) return true;
            if (!Config.allowLoadoutChangeAtGate || !IsMatchLive || !player.IsActiveInMatch) return false;
            if (player.SecondsSinceRedeploy < Config.loadoutSecondsAfterRedeploy) return true;
            return Player.TeamSpawnPoint.InZone(player.Team, player.Position, Config.gateRadius);
        }

        public float? MatchTimeRemaining => MatchTimer.RemainingTime(Runner);
        public float? PhaseTimeRemaining => PhaseTimer.RemainingTime(Runner);
        public float MatchElapsedSeconds => Runner != null && MatchStartTick > 0 ? ((int)Runner.Tick - MatchStartTick) * Runner.DeltaTime : 0f;
        public IReadOnlyList<ExtractionPoint> ExtractionPoints => extractionPoints;

        public override void Spawned()
        {
            Instance = this;
            changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            // (v0.8) editor-only fallbacks for references the scene builder normally fills (see Core.DevAssetFallback).
            if (config == null) config = Core.DevAssetFallback.Load<Data.GameModeConfig>(Core.DevAssetFallback.ConfigPath, nameof(ExtractionHeistGameMode), nameof(config));
            if (lootDropPrefabObject == null && !lootDropPrefab.IsValid) lootDropPrefabObject = Core.DevAssetFallback.LoadPrefab(Core.DevAssetFallback.LootDropPrefabPath, nameof(ExtractionHeistGameMode), nameof(lootDropPrefabObject));
            if (idCardPrefabObject == null) idCardPrefabObject = Core.DevAssetFallback.LoadPrefab(Core.DevAssetFallback.IDCardPrefabPath, nameof(ExtractionHeistGameMode), nameof(idCardPrefabObject));

            if (config == null)
            {
                Debug.LogError("[ExtractionHeist] No GameModeConfig assigned on ExtractionHeistGameMode: using built-in defaults " +
                               "(minPlayersToStart = 8, so a solo match never starts). Assign Assets/Ouroboros/DevGameModeConfig.asset " +
                               "or re-run Ouroboros > Setup > Create Dev Scene.");
            }
            else
            {
                Debug.Log($"[ExtractionHeist] Config '{config.name}': autoStart={config.autoStart}, minPlayers={config.minPlayersToStart}, " +
                          $"countdown={config.preMatchCountdown}s, classChangeBeforeMatch={config.allowClassChangeBeforeMatch}");
            }

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
                    if (CurrentState != GameState.InProgress) break; // the timer just started the getaway
                    if (Config.UseRunRules)
                    {
                        // (v0.10) no exits until a crew is carrying the quota: that crew calls the getaway for everyone
                        CheckQuota();
                    }
                    else if (!ExtractionOpen && MatchElapsedSeconds >= Config.extractionPointActivationDelay)
                    {
                        OpenExtraction(); // [v0.9] rules: exits open on a timer
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
                        if (Config.UseRunRules) UpdateRichestCrew(); // (v0.10)
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
        // The Run (v0.10): quota race and getaway
        // ------------------------------------------------------------------

        private readonly int[] carriedScratch = new int[Core.GameConstants.MAX_TEAMS];
        private readonly bool[] activeScratch = new bool[Core.GameConstants.MAX_TEAMS];

        /// <summary>Value each crew's living members are carrying right now.</summary>
        private void SumCarried()
        {
            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++) { carriedScratch[i] = 0; activeScratch[i] = false; }
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null) continue;
                int idx = Core.TeamUtil.ToIndex(p.Team);
                if (idx < 0 || idx >= Core.GameConstants.MAX_TEAMS) continue;
                activeScratch[idx] = true;
                if (p.IsActiveInMatch) carriedScratch[idx] += p.CarriedLoot;
            }
        }

        /// <summary>Value <paramref name="team"/>'s living members are carrying. Works on every peer.</summary>
        public int GetTeamCarried(Core.TeamID team)
        {
            int total = 0;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p != null && p.Object != null && p.Team == team && p.IsActiveInMatch) total += p.CarriedLoot;
            }
            return total;
        }

        // ------------------------------------------------------------------
        // Cash-out stations (v0.16)

        /// <summary>Raised on the state authority when a station's pot is banked (team, value banked).</summary>
        public static event Action<Core.TeamID, int> CashBanked;

        // ------------------------------------------------------------------
        // Crew perks (v0.21): taken from Grand Vaults, held by the whole crew until the match ends

        /// <summary>One bit per <see cref="Core.VaultPerk"/> a crew holds. See <see cref="Core.VaultRules"/>.</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)]
        public NetworkArray<int> TeamPerks => default;

        /// <summary>The perks <paramref name="team"/> holds, as a bit mask. Safe to call on any peer, with no match running.</summary>
        public static int PerksOf(Core.TeamID team)
        {
            var gm = Instance;
            int idx = Core.TeamUtil.ToIndex(team);
            if (gm == null || gm.Object == null || idx < 0 || idx >= Core.GameConstants.MAX_TEAMS) return 0;
            return gm.TeamPerks[idx];
        }

        /// <summary>Gives <paramref name="team"/> a perk for the rest of the match. State authority only.</summary>
        public bool GrantTeamPerk(Core.TeamID team, Core.VaultPerk perk)
        {
            int idx = Core.TeamUtil.ToIndex(team);
            if (!Object.HasStateAuthority || perk == Core.VaultPerk.None || idx < 0 || idx >= Core.GameConstants.MAX_TEAMS) return false;
            if (Core.VaultRules.Has(TeamPerks[idx], perk)) return false;
            TeamPerks.Set(idx, Core.VaultRules.With(TeamPerks[idx], perk));
            return true;
        }

        /// <summary>What <paramref name="team"/> has safely banked so far. Works on every peer.</summary>
        public int GetTeamBanked(Core.TeamID team)
        {
            int idx = Core.TeamUtil.ToIndex(team);
            return idx >= 0 && idx < Core.GameConstants.MAX_TEAMS ? TeamLoot[idx] : 0;
        }

        /// <summary>Banked plus carried: what the crew has toward the getaway quota. Works on every peer.</summary>
        public int GetTeamHaul(Core.TeamID team)
        {
            return Core.MatchRules.CrewHaul(GetTeamCarried(team), GetTeamBanked(team), Config.quotaCountsBanked);
        }

        /// <summary>
        /// Banks a finished cash-out for <paramref name="team"/>. Unlike an exit, nobody leaves the match: the
        /// money is safe and the crew plays on. Returns the value banked. State authority only.
        /// </summary>
        public int BankCashOut(Core.TeamID team, int potValue)
        {
            if (!Object.HasStateAuthority || !Core.TeamUtil.IsValid(team) || potValue <= 0) return 0;
            int idx = Core.TeamUtil.ToIndex(team);
            int banked = Core.MatchRules.BankedValue(potValue, Config.cashOutPayout);
            if (banked <= 0) return 0;

            TeamLoot.Set(idx, TeamLoot[idx] + banked);
            // Under run rules the score is the money. Under the older rules loot is worth points at the usual rate.
            AwardTeamScore(team, Config.scoreIsBankedLootOnly ? banked : Mathf.RoundToInt(banked * Config.pointsPerLoot));
            CashBanked?.Invoke(team, banked);
            return banked;
        }

        private void CheckQuota()
        {
            if (((int)Runner.Tick) % 10 != 0) return;
            SumCarried();
            for (int step = 0; step < Core.GameConstants.MAX_TEAMS; step++)
            {
                int i = Core.MatchRules.RotatedIndex((int)Runner.Tick, step, Core.GameConstants.MAX_TEAMS);
                // [v0.13] if (activeScratch[i] && Core.MatchRules.QuotaReached(carriedScratch[i], Config.lootQuota))
                // [v0.15] if (activeScratch[i] && Core.MatchRules.QuotaReached(carriedScratch[i], LootQuota)) // (v0.14) a map can set its own quota
                // (v0.16) what a crew has banked at a station counts toward the quota too
                int haul = Core.MatchRules.CrewHaul(carriedScratch[i], TeamLoot[i], Config.quotaCountsBanked);
                if (activeScratch[i] && Core.MatchRules.QuotaReached(haul, LootQuota))
                {
                    EnterExtractionPhase(Core.TeamUtil.FromIndex(i));
                    return;
                }
            }
        }

        /// <summary>During the getaway: keep track of who is richest and keep their carriers lit up.</summary>
        private void UpdateRichestCrew()
        {
            if (((int)Runner.Tick) % 30 != 0) return;
            SumCarried();
            int richest = Core.MatchRules.RichestTeam(carriedScratch, activeScratch);
            RichestTeam = richest >= 0 ? Core.TeamUtil.FromIndex(richest) : Core.TeamID.None;
            if (richest < 0 || !Config.revealRichestCrew) return;

            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                // [v0.17] if (p.Team == RichestTeam && p.CarriedLoot > 0) p.ApplyTimedStatus(Core.StatusFlags.Revealed, 1.5f);
                if (p.Team == RichestTeam && p.CarriedLoot > 0)
                {
                    p.ApplyTimedStatus(Core.StatusFlags.Revealed, 1.5f);
                    p.MarkFor(Core.TeamID.None, 1.5f); // (v0.18) the one mark every crew sees
                }
            }
        }

        /// <summary>
        /// Spawns a loot pickup. <paramref name="itemId"/> is a loot catalog id (0 = loose loot of
        /// <paramref name="value"/>). Returns false when no pickup prefab is assigned. State authority only.
        /// </summary>
        public bool SpawnLoot(Vector3 position, int value, int itemId, float lifetimeSeconds = 0f)
        {
            if (!Object.HasStateAuthority || value <= 0 || !CanSpawnLoot) return false;
            void Init(NetworkRunner runner, NetworkObject obj)
            {
                var drop = obj.GetComponent<LootDrop>();
                if (drop != null) drop.Configure(value, itemId, lifetimeSeconds);
            }
            if (lootDropPrefab.IsValid) Runner.Spawn(lootDropPrefab, position, Quaternion.identity, null, Init);
            else Runner.Spawn(lootDropPrefabObject, position, Quaternion.identity, null, Init);
            return true;
        }

        /// <summary>
        /// Decides when (and whether) a player who just died comes back, and resolves a crew wipe if they were
        /// the last one standing. Called by <see cref="Network.NetworkPlayer"/> right after <see cref="OnPlayerDied"/>.
        /// </summary>
        public void ScheduleRespawn(Network.NetworkPlayer victim)
        {
            if (!Object.HasStateAuthority || victim == null) return;
            if (!CanRespawn(victim)) return;

            int idx = Core.TeamUtil.ToIndex(victim.Team);
            bool crewRules = Config.respawnRequiresLivingCrewmate && idx >= 0 && idx < Core.GameConstants.MAX_TEAMS;
            if (!crewRules)
            {
                victim.RespawnTimer = TickTimer.CreateFromSeconds(Runner, Config.respawnDelay); // [v0.9] behaviour
                return;
            }

            if (CrewEliminated[idx]) return;

            bool crewmateAlive = false;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p == victim || p.Object == null || p.Team != victim.Team) continue;
                if (p.IsActiveInMatch) { crewmateAlive = true; break; }
            }

            if (crewmateAlive)
            {
                victim.RespawnTimer = TickTimer.CreateFromSeconds(Runner, Config.respawnDelay);
                return;
            }

            // The whole crew is down at once: a wipe.
            var outcome = Core.MatchRules.ResolveCrewWipe(CrewWipes[idx], Config.maxCrewWipes);
            CrewWipes.Set(idx, CrewWipes[idx] + 1);
            bool eliminated = outcome == Core.MatchRules.WipeOutcome.Eliminated;
            if (eliminated) CrewEliminated.Set(idx, true);

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || p.Team != victim.Team || p.IsAlive || p.IsExtracted) continue;
                p.RespawnTimer = eliminated ? TickTimer.None : TickTimer.CreateFromSeconds(Runner, Config.crewWipeRedeployDelay);
            }
            RPC_CrewWiped(victim.Team, eliminated);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_CrewWiped(Core.TeamID team, NetworkBool eliminated)
        {
            Debug.Log($"[ExtractionHeist] {Core.TeamUtil.DisplayName(team)} crew wiped" + (eliminated ? " and is out of the match" : ", redeploying"));
            CrewWiped?.Invoke(team, eliminated);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_GetawayCalled(Core.TeamID byTeam)
        {
            Debug.Log(byTeam != Core.TeamID.None
                ? $"[ExtractionHeist] {Core.TeamUtil.DisplayName(byTeam)} crew hit the quota: the getaway is called"
                : "[ExtractionHeist] Time is up: the getaway is called");
            GetawayCalled?.Invoke(byTeam);
        }

        // ------------------------------------------------------------------
        // Match flow
        // ------------------------------------------------------------------

        /// <summary>Called by the session manager whenever the player count changes (state authority only).</summary>
        public void NotifyPlayerCountChanged(int playerCount)
        {
            if (!Object.HasStateAuthority) return;
            bool canAuto = CurrentState == GameState.WaitingForPlayers && Config.autoStart && playerCount >= Mathf.Max(1, Config.minPlayersToStart);
            Debug.Log($"[ExtractionHeist] Players: {playerCount}, state: {CurrentState}, autoStart: {Config.autoStart}, need: {Config.minPlayersToStart} → " +
                      (canAuto ? "starting countdown" : "waiting (use the lobby's Start button)"));
            if (canAuto)
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
                CrewWipes.Set(i, 0);          // (v0.10)
                CrewEliminated.Set(i, false); // (v0.10)
                TeamLoot.Set(i, 0);
                TeamPerks.Set(i, 0);          // (v0.21)
                TeamObjectives.Set(i, 0);
                TeamExtractedCount.Set(i, 0);
                TeamExtracted.Set(i, false);
            }

            TailTeam = Core.TeamID.None;
            RichestTeam = Core.TeamID.None;
            // (v0.10) empty crew seats are taken by bots so the match is always playable
            if (Config.fillWithBots) Network.GameSessionManager.Instance?.FillWithBots();

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

        // [v0.9] private void EnterExtractionPhase()
        /// <summary>
        /// Starts the getaway. <paramref name="calledBy"/> is the crew whose haul triggered it (v0.10), or None
        /// when the match timer ran out.
        /// </summary>
        private void EnterExtractionPhase(Core.TeamID calledBy = Core.TeamID.None)
        {
            CurrentState = GameState.Extraction;
            TailTeam = calledBy;
            RichestTeam = calledBy;
            if (!ExtractionOpen) OpenExtraction();
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, Config.extractionWindowDuration);
            RPC_AnnounceExtractionPhase(Config.extractionWindowDuration);
            if (Config.UseRunRules)
            {
                if (Config.lockdownOnGetaway) AlarmSystem.Raise(100f, "getaway");
                RPC_GetawayCalled(calledBy);
            }
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
            ExtractionOpened?.Invoke();
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
            // [v0.9] AwardTeamScore(team, Config.pointsPerObjective);
            if (!Config.scoreIsBankedLootOnly) AwardTeamScore(team, Config.pointsPerObjective); // (v0.10) only extracted loot scores
        }

        /// <summary>Kill credit and loot drop. Called by <see cref="Network.NetworkPlayer"/> on death.</summary>
        public void OnPlayerDied(Network.NetworkPlayer victim, Network.NetworkPlayer killer)
        {
            if (!Object.HasStateAuthority || victim == null) return;

            // [v0.9] if (killer != null && killer != victim && killer.Team != victim.Team)
            if (!Config.scoreIsBankedLootOnly && killer != null && killer != victim && killer.Team != victim.Team)
            {
                AwardTeamScore(killer.Team, Config.pointsPerKill);
            }
            AlarmSystem.Raise(Config.alarmOnKill, "gunfire");

            if (idCardPrefabObject != null)
            {
                var team = victim.Team;
                var victimId = victim.Object.Id; // (v0.17) the card knows whose it is, so a crewmate can revive them with it
                // [v0.16] Runner.Spawn(idCardPrefabObject, victim.transform.position + Vector3.up * 0.3f + Vector3.right * 0.6f, Quaternion.identity, null,
                // [v0.16]     (runner, obj) => { var card = obj.GetComponent<Interaction.IDCardDrop>(); if (card != null) card.OwnerTeam = team; });
                Runner.Spawn(idCardPrefabObject, victim.transform.position + Vector3.up * 0.3f + Vector3.right * 0.6f, Quaternion.identity, null,
                    (runner, obj) => { var card = obj.GetComponent<Interaction.IDCardDrop>(); if (card != null) { card.OwnerTeam = team; card.OwnerId = victimId; } });
            }

            // (v0.10) named items fall as separate pickups around the body; whatever is left over is loose loot
            deathItemScratch.Clear();
            int loot = victim.TakeAllLoot(deathItemScratch);
            if (loot > 0 && Config.dropLootOnDeath && CanSpawnLoot)
            {
                Vector3 centre = victim.transform.position + Vector3.up * 0.5f;
                var catalog = Data.LootCatalog.Active;
                int remaining = loot;
                for (int i = 0; i < deathItemScratch.Count; i++)
                {
                    int id = deathItemScratch[i];
                    int value = Mathf.Min(remaining, catalog.ValueOf(id));
                    if (id <= 0 || value <= 0) continue;
                    float angle = (i / (float)Mathf.Max(1, deathItemScratch.Count)) * Mathf.PI * 2f;
                    SpawnLoot(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.2f, value, id);
                    remaining -= value;
                }
                if (remaining > 0) SpawnLoot(centre, remaining, 0);
            }
            // [v0.9] int loot = victim.TakeAllLoot();
            // [v0.9] if (loot > 0 && Config.dropLootOnDeath && (lootDropPrefab.IsValid || lootDropPrefabObject != null))
            // [v0.9] {
            // [v0.9]     Vector3 pos = victim.transform.position + Vector3.up * 0.5f;
            // [v0.9]     void InitDrop(NetworkRunner runner, NetworkObject obj)
            // [v0.9]     {
            // [v0.9]         var drop = obj.GetComponent<LootDrop>();
            // [v0.9]         if (drop != null) drop.Value = loot;
            // [v0.9]     }
            // [v0.9]     if (lootDropPrefab.IsValid) Runner.Spawn(lootDropPrefab, pos, Quaternion.identity, null, InitDrop);
            // [v0.9]     else Runner.Spawn(lootDropPrefabObject, pos, Quaternion.identity, null, InitDrop);
            // [v0.9] }
        }

        private readonly List<int> deathItemScratch = new List<int>(Network.NetworkPlayer.MAX_CARRIED_ITEMS);

        /// <summary>Whether a dead player is allowed another respawn under the current rules.</summary>
        public bool CanRespawn(Network.NetworkPlayer player)
        {
            if (player == null || player.IsExtracted) return false;
            if (!Config.allowRespawning) return false;
            if (CurrentState == GameState.MatchEnded) return false;
            if (CurrentState == GameState.Extraction && !Config.respawnDuringExtractionPhase) return false;
            int crew = Core.TeamUtil.ToIndex(player.Team);
            if (crew >= 0 && crew < Core.GameConstants.MAX_TEAMS && CrewEliminated[crew]) return false; // (v0.10)
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
            OnTeamExtractionCompleted(team, members, 1f);
        }

        /// <summary>
        /// (v0.10) As above, through an exit that pays <paramref name="payoutMultiplier"/> of the carried value
        /// (1 at the central getaway, less at side exits).
        /// </summary>
        public void OnTeamExtractionCompleted(Core.TeamID team, List<Network.NetworkPlayer> members, float payoutMultiplier)
        {
            if (!Object.HasStateAuthority || !Core.TeamUtil.IsValid(team)) return;

            int idx = Core.TeamUtil.ToIndex(team);
            int bankedLoot = 0;
            int extractedNow = 0;

            int caseLoot = 0;
            foreach (var member in members)
            {
                if (member == null || !CanExtract(member)) continue;
                bankedLoot += member.TakeAllLoot();
                // v0.5: carried cases are banked with their carrier
                for (int c = LootCase.All.Count - 1; c >= 0; c--)
                {
                    var lootCase = LootCase.All[c];
                    if (lootCase != null && lootCase.Object != null) caseLoot += lootCase.BankFor(member);
                }
                member.MarkExtracted();
                extractedNow++;
            }

            if (extractedNow == 0) return;

            int points;
            if (Config.scoreIsBankedLootOnly)
            {
                // (v0.10) the score is what you got out with: carried value, reduced at side exits
                bankedLoot = Core.MatchRules.BankedValue(bankedLoot, payoutMultiplier);
                caseLoot = Core.MatchRules.BankedValue(Mathf.RoundToInt(caseLoot * Config.pointsPerCaseLoot), payoutMultiplier);
                points = bankedLoot + caseLoot;
                TeamLoot.Set(idx, TeamLoot[idx] + points);
                TeamExtractedCount.Set(idx, TeamExtractedCount[idx] + extractedNow);
                bankedLoot += caseLoot;
                if (!TeamExtracted[idx]) TeamExtracted.Set(idx, true);
            }
            else
            {
                // [v0.9] scoring: members, loot, case multiplier and a first-extraction bonus
                TeamLoot.Set(idx, TeamLoot[idx] + bankedLoot + caseLoot);
                TeamExtractedCount.Set(idx, TeamExtractedCount[idx] + extractedNow);

                points = extractedNow * Config.pointsPerTeamMemberExtracted
                         + Mathf.RoundToInt(bankedLoot * Config.pointsPerLoot)
                         + Mathf.RoundToInt(caseLoot * Config.pointsPerCaseLoot);
                bankedLoot += caseLoot;
                if (!TeamExtracted[idx])
                {
                    // First successful extraction for this team
                    TeamExtracted.Set(idx, true);
                    points += Config.pointsPerExtraction;
                }
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
            TeamExtractedEvent?.Invoke(team, members, loot);
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
            // (v0.9) the comparison lives in Core.MatchRules.PickWinner so it can be unit-tested outside Unity.
            const int n = Core.GameConstants.MAX_TEAMS;
            var scores = new int[n]; var loot = new int[n]; var extracted = new int[n]; var hasPlayers = new bool[n];
            var tm = Network.TeamManager.Instance;
            for (int i = 0; i < n; i++)
            {
                scores[i] = TeamScores[i];
                loot[i] = TeamLoot[i];
                extracted[i] = TeamExtractedCount[i];
                hasPlayers[i] = tm == null || tm.GetTeamPlayerCount(Core.TeamUtil.FromIndex(i)) > 0;
            }
            int winner = Core.MatchRules.PickWinner(scores, loot, extracted, hasPlayers);
            return winner >= 0 ? Core.TeamUtil.FromIndex(winner) : Core.TeamID.None;

            // [v0.8] Core.TeamID best = Core.TeamID.None;
            // [v0.8] int bestScore = int.MinValue, bestLoot = int.MinValue, bestExtracted = int.MinValue;
            // [v0.8] for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            // [v0.8] {
            // [v0.8]     var tm = Network.TeamManager.Instance;
            // [v0.8]     if (tm != null && tm.GetTeamPlayerCount(Core.TeamUtil.FromIndex(i)) == 0) continue;
            // [v0.8]     int score = TeamScores[i], loot = TeamLoot[i], extracted = TeamExtractedCount[i];
            // [v0.8]     bool better = score > bestScore
            // [v0.8]                   || (score == bestScore && loot > bestLoot)
            // [v0.8]                   || (score == bestScore && loot == bestLoot && extracted > bestExtracted);
            // [v0.8]     if (better)
            // [v0.8]     {
            // [v0.8]         best = Core.TeamUtil.FromIndex(i);
            // [v0.8]         bestScore = score; bestLoot = loot; bestExtracted = extracted;
            // [v0.8]     }
            // [v0.8] }
            // [v0.8] return best;
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
