using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// A heist objective: a vault, data cache or safe that a team cracks by holding Interact inside the
    /// zone. On completion the team scores and the loot is split between the players who worked it.
    /// Carried loot is only banked when the carrier extracts; dying drops it.
    ///
    /// Hackers can <see cref="OnHacked"/> it for a progress boost (or to unlock it when
    /// <see cref="requiresHack"/> is set); enemies inside the zone contest and pause progress.
    /// </summary>
    public class LootObjective : NetworkBehaviour, Interaction.IHackable
    {
        /// <summary>Live registry on this peer (HUD proximity lookups).</summary>
        public static readonly List<LootObjective> All = new List<LootObjective>();
        /// <summary>Fired on every peer when an objective is secured (objective, team, loot).</summary>
        public static event System.Action<LootObjective, Core.TeamID, int> Completed;

        [Header("Objective")]
        [SerializeField] private string objectiveName = "Vault";
        [SerializeField] private int lootValue = 500;
        [SerializeField] private float captureTime = 8f;
        [SerializeField] private float interactRadius = 3f;
        [SerializeField] private bool respawns = true;
        [Tooltip("Overrides GameModeConfig.objectiveRespawnTime when > 0.")]
        [SerializeField] private float respawnTimeOverride = 0f;
        [Tooltip("If set, the objective must be hacked (Hacker ability) before it can be captured.")]
        [SerializeField] private bool requiresHack = false;
        [Tooltip("Fraction of captureTime granted instantly by a successful hack.")]
        [Range(0f, 1f)] [SerializeField] private float hackProgressBonus = 0.5f;
        [Tooltip("Only count objective completions once the match is InProgress / Extraction.")]
        [SerializeField] private bool onlyDuringMatch = true;
        [Tooltip("(v0.5) Number of cracking passes needed. Each intermediate stage resets progress and raises the alarm.")]
        [SerializeField] private int stages = 1;
        [Tooltip("(v0.7) Exterior vault door that must be open before this interior can be cracked (also blocks hacking through walls).")]
        [SerializeField] private Interaction.VaultDoor gatedBy;

        [Header("Loot (v0.10)")]
        [Tooltip("Named items this vault spills when cracked. Their catalog values replace lootValue. 0 = old behaviour (lootValue split between the crackers).")]
        [SerializeField] private int lootItemCount = 3;
        [Tooltip("Which tier of the loot catalog most of this vault's items come from.")]
        [SerializeField] private Data.LootTier lootTier = Data.LootTier.Common;
        [Tooltip("Also add one item from the next tier up (a reason to pick this vault).")]
        [SerializeField] private bool bonusItemFromNextTier = false;
        [Tooltip("Seconds spilled items stay before they disappear.")]
        [SerializeField] private float lootLifetime = 240f;

        [Networked] public NetworkBool IsAvailable { get; set; }
        [Networked] public NetworkBool IsUnlocked { get; set; }
        [Networked] public Core.TeamID CapturingTeam { get; set; }
        [Networked] public float Progress { get; set; }
        [Networked] public NetworkBool IsContested { get; set; }
        [Networked] public TickTimer RespawnTimer { get; set; }
        [Networked] public TickTimer HackUnlockTimer { get; set; }
        [Networked] public int StagesDone { get; set; }

        private readonly List<Network.NetworkPlayer> interacting = new List<Network.NetworkPlayer>();
        private readonly List<Network.NetworkPlayer> present = new List<Network.NetworkPlayer>();

        public string ObjectiveName => objectiveName;
        public int LootValue => lootValue;
        public float InteractRadius => interactRadius;
        public int Stages => Mathf.Max(1, stages);
        public bool IsGated => gatedBy != null && gatedBy.Object != null && !gatedBy.IsOpen;
        public Interaction.VaultDoor GatedBy => gatedBy;
        public float ProgressNormalized => Mathf.Clamp01(Progress / Mathf.Max(0.01f, captureTime));

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            if (Object.HasStateAuthority)
            {
                IsAvailable = true;
                IsUnlocked = !requiresHack;
                CapturingTeam = Core.TeamID.None;
                Progress = 0f;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (!IsAvailable)
            {
                if (RespawnTimer.IsRunning && RespawnTimer.Expired(Runner))
                {
                    RespawnTimer = TickTimer.None;
                    IsAvailable = true;
                    IsUnlocked = !requiresHack;
                    RPC_Respawned();
                }
                return;
            }

            if (requiresHack && IsUnlocked && HackUnlockTimer.IsRunning && HackUnlockTimer.Expired(Runner))
            {
                // Temporary hack unlock expired
                HackUnlockTimer = TickTimer.None;
                IsUnlocked = false;
            }

            var gm = ExtractionHeistGameMode.Instance;
            if (onlyDuringMatch && (gm == null || !gm.IsMatchLive))
            {
                if (CapturingTeam != Core.TeamID.None) Cancel();
                return;
            }

            if (IsGated)
            {
                if (CapturingTeam != Core.TeamID.None) Cancel();
                return;
            }

            Scan();

            if (!IsUnlocked)
            {
                if (CapturingTeam != Core.TeamID.None) Cancel();
                return;
            }

            if (CapturingTeam == Core.TeamID.None)
            {
                // [v0.9] Lowest team id with an interacting member starts the capture (deterministic)
                // (v0.10) the scan starts at a different crew every tick, so no crew wins every simultaneous claim
                for (int step = 0; step < Core.GameConstants.MAX_TEAMS; step++)
                {
                    int t = Core.MatchRules.RotatedIndex((int)Runner.Tick, step, Core.GameConstants.MAX_TEAMS);
                    Core.TeamID team = Core.TeamUtil.FromIndex(t);
                    foreach (var p in interacting)
                    {
                        if (p.Team == team)
                        {
                            CapturingTeam = team;
                            Progress = 0f;
                            RPC_CaptureStarted(team);
                            break;
                        }
                    }
                    if (CapturingTeam != Core.TeamID.None) break;
                }
                return;
            }

            int workers = 0;
            bool enemyPresent = false;
            foreach (var p in interacting) if (p.Team == CapturingTeam) workers++;
            foreach (var p in present) if (p.Team != CapturingTeam) enemyPresent = true;

            if (enemyPresent != IsContested) IsContested = enemyPresent;

            if (workers == 0)
            {
                // [v0.10] Cancel(); return;   // stepping away for a second threw the whole crack away
                // (v0.11) an abandoned crack drains instead; the crew keeps the vault until it reaches zero
                Progress -= Runner.DeltaTime * Core.HackRules.AbandonDecayPerSecond;
                if (Progress <= 0f) Cancel();
                return;
            }

            // [v0.10] if (enemyPresent) return;   // (v0.11) folded into PassiveRate: sign answers still count while contested

            // Extra workers speed the crack up slightly (diminishing returns).
            // [v0.8] float rate = 1f + 0.25f * Mathf.Min(workers - 1, 3);
            // [v0.10] float rate = Core.MatchRules.CrackRate(workers); // (v0.9) same formula, unit-tested
            // (v0.11) being jacked in only trickles; answering signs (ApplyHackAnswer) is what cracks a vault quickly
            float rate = Core.HackRules.PassiveRate(workers, enemyPresent);
            Progress += Runner.DeltaTime * rate;

            if (Progress >= captureTime)
            {
                Complete(gm);
            }
        }

        private void Scan()
        {
            interacting.Clear();
            present.Clear();
            float radiusSq = interactRadius * interactRadius;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                if ((p.transform.position - transform.position).sqrMagnitude > radiusSq) continue;
                present.Add(p);
                // [v0.10] if (p.HasStatus(Core.StatusFlags.Interacting) && !p.HasStatus(Core.StatusFlags.EMPDisabled))
                // (v0.11) working a vault means being jacked into it (press Interact once), not holding a key
                if (p.JackStation == Object.Id && !p.HasStatus(Core.StatusFlags.EMPDisabled))
                {
                    interacting.Add(p);
                }
            }
        }

        private void Cancel()
        {
            Core.TeamID prev = CapturingTeam;
            CapturingTeam = Core.TeamID.None;
            Progress = 0f;
            IsContested = false;
            if (prev != Core.TeamID.None) RPC_CaptureCancelled(prev);
        }

        private void Complete(ExtractionHeistGameMode gm)
        {
            Core.TeamID team = CapturingTeam;

            // v0.5: multi-stage vaults
            if (StagesDone + 1 < Stages)
            {
                StagesDone++;
                Progress = 0f;
                IsContested = false;
                AlarmSystem.Raise(gm != null ? gm.Config.alarmOnObjectiveStage : 10f, objectiveName);
                RPC_StageDone(team, StagesDone, Stages);
                return;
            }

            // (v0.10) the vault spills named items onto the floor; whoever picks them up carries them. With no
            //         pickup prefab or no items configured, fall back to handing the crackers a number.
            int payout = SpillLoot(gm);
            if (payout <= 0)
            {
                payout = lootValue;
                // Split loot between the workers; remainder goes to the first.
                int workers = 0;
                foreach (var p in interacting) if (p.Team == team) workers++;
                // [v0.8] int share = workers > 0 ? lootValue / workers : lootValue;
                // [v0.8] int remainder = workers > 0 ? lootValue - share * workers : 0;
                Core.MatchRules.SplitLoot(lootValue, workers, out int share, out int remainder); // (v0.9) same split, unit-tested
                bool first = true;
                foreach (var p in interacting)
                {
                    if (p.Team != team) continue;
                    p.AddLoot(share + (first ? remainder : 0));
                    first = false;
                }

            }

            gm?.OnObjectiveCompleted(team, this);
            AlarmSystem.Raise(gm != null ? gm.Config.alarmOnObjectiveCompleted : 20f, objectiveName);

            CapturingTeam = Core.TeamID.None;
            Progress = 0f;
            IsContested = false;
            IsAvailable = false;
            StagesDone = 0;

            float respawnTime = respawnTimeOverride > 0f ? respawnTimeOverride : (gm != null ? gm.Config.objectiveRespawnTime : 60f);
            if (respawns && respawnTime > 0f)
            {
                RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnTime);
            }

            // [v0.9] RPC_Completed(team, lootValue);
            RPC_Completed(team, payout);
        }

        // ------------------------------------------------------------------
        // Deep hack (v0.11)

        /// <summary>Fired on every peer when a rival crew counter-hacks a vault away from its owner (objective, new owner).</summary>
        public static event System.Action<LootObjective, Core.TeamID> TakenOver;

        /// <summary>How hard this vault's signs are to read: its loot tier.</summary>
        public int HackDifficulty => (int)lootTier;
        public int HackDifficultyFor(Network.NetworkPlayer player) =>
            Core.HackRules.EffectiveDifficulty(HackDifficulty, player != null && player.IsHackSpecialist);
        public float CaptureTime => captureTime;

        /// <summary>Whether <paramref name="player"/> may be (or stay) jacked into this vault.</summary>
        public bool CanJackIn(Network.NetworkPlayer player)
        {
            if (player == null || Object == null || !IsAvailable || !IsUnlocked || IsGated) return false;
            if (!player.IsActiveInMatch) return false;
            var gm = ExtractionHeistGameMode.Instance;
            if (onlyDuringMatch && (gm == null || gm.Object == null || !gm.IsMatchLive)) return false;
            return (player.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;
        }

        /// <summary>The nearest vault <paramref name="player"/> is standing at and could jack into.</summary>
        public static LootObjective FindJackable(Network.NetworkPlayer player)
        {
            LootObjective best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var obj = All[i];
                if (obj == null || !obj.CanJackIn(player)) continue;
                float dSq = (obj.transform.position - player.transform.position).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; best = obj; }
            }
            return best;
        }

        /// <summary>
        /// Applies one sign-puzzle answer from a jacked-in player. A right answer drives their crew's crack
        /// forward, or, on a vault another crew holds, drives that crew back and can take the vault over.
        /// A wrong answer costs a little progress and raises the alarm. State authority only.
        /// </summary>
        public void ApplyHackAnswer(Network.NetworkPlayer by, bool correct)
        {
            if (!Object.HasStateAuthority || by == null || !CanJackIn(by)) return;
            var gm = ExtractionHeistGameMode.Instance;

            if (!correct)
            {
                if (by.Team == CapturingTeam) Progress = Mathf.Max(0f, Progress - Core.HackRules.WrongLoss(captureTime));
                AlarmSystem.Raise(gm != null ? gm.Config.alarmOnHackMistake : 6f, objectiveName);
                return;
            }

            float gain = Core.HackRules.CorrectGain(captureTime, by.IsHackSpecialist);
            if (CapturingTeam == Core.TeamID.None)
            {
                CapturingTeam = by.Team;
                Progress = 0f;
                RPC_CaptureStarted(by.Team);
            }

            if (by.Team == CapturingTeam)
            {
                Progress += gain;
                if (Progress >= captureTime)
                {
                    Scan(); // the loot fallback and worker count read the jacked-in list
                    Complete(gm);
                }
                return;
            }

            // Counter-hack: push the owner back; past zero the vault changes hands.
            Core.HackRules.ApplyCounterHack(Progress, gain, out float newProgress, out bool changedHands);
            Progress = newProgress;
            if (changedHands)
            {
                CapturingTeam = by.Team;
                RPC_TakenOver(by.Team);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TakenOver(Core.TeamID team)
        {
            Debug.Log($"[Objective:{objectiveName}] counter-hacked: now held by {Core.TeamUtil.DisplayName(team)}");
            TakenOver?.Invoke(this, team);
        }

        private readonly List<int> tierIds = new List<int>();
        private readonly int[] picks = new int[Network.NetworkPlayer.MAX_CARRIED_ITEMS];

        /// <summary>
        /// (v0.10) Spawns this vault's items in a ring around it and returns their total value (0 when nothing
        /// could be spawned). Which items come out is a deterministic pick from the catalog tier.
        /// </summary>
        private int SpillLoot(ExtractionHeistGameMode gm)
        {
            if (gm == null || !gm.CanSpawnLoot || lootItemCount <= 0) return 0;
            var catalog = Data.LootCatalog.Active;
            if (catalog == null || catalog.Count == 0) return 0;

            int seed = unchecked((int)Runner.Tick * 397 ^ objectiveName.GetHashCode());
            int spawned = 0, total = 0;

            void Spill(Data.LootTier tier, int count, int tierSeed)
            {
                catalog.GetIdsOfTier(tier, tierIds);
                int n = Core.MatchRules.PickDistinct(tierSeed, tierIds.Count, Mathf.Min(count, picks.Length), picks);
                for (int i = 0; i < n; i++)
                {
                    int id = tierIds[picks[i]];
                    int value = catalog.ValueOf(id);
                    float angle = spawned * 2.399963f; // golden angle: an even ring for any count
                    Vector3 pos = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.7f;
                    pos.y = transform.position.y - 0.4f;
                    if (gm.SpawnLoot(pos, value, id, lootLifetime)) { spawned++; total += value; }
                }
            }

            Spill(lootTier, lootItemCount, seed);
            if (bonusItemFromNextTier && lootTier != Data.LootTier.Legendary) Spill(lootTier + 1, 1, seed + 1);
            return total;
        }

        // ---- IHackable ----
        public void OnHacked(Core.TeamID byTeam, float duration)
        {
            if (!Object.HasStateAuthority || !IsAvailable || IsGated) return;

            if (requiresHack && !IsUnlocked)
            {
                IsUnlocked = true;
                HackUnlockTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(duration, captureTime * 2f));
                RPC_Hacked(byTeam, true);
                return;
            }

            if (CapturingTeam == Core.TeamID.None || CapturingTeam == byTeam)
            {
                if (CapturingTeam == Core.TeamID.None)
                {
                    CapturingTeam = byTeam;
                    RPC_CaptureStarted(byTeam);
                }
                Progress = Mathf.Min(captureTime, Progress + captureTime * hackProgressBonus);
                RPC_Hacked(byTeam, false);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_CaptureStarted(Core.TeamID team) => Debug.Log($"[Objective:{objectiveName}] {team} started cracking");

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_CaptureCancelled(Core.TeamID team) => Debug.Log($"[Objective:{objectiveName}] {team} capture cancelled");

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StageDone(Core.TeamID team, int done, int total) => Debug.Log($"[Objective:{objectiveName}] {team} completed stage {done}/{total}");

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Completed(Core.TeamID team, int loot)
        {
            Debug.Log($"[Objective:{objectiveName}] {team} secured {loot} loot!");
            Completed?.Invoke(this, team, loot);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Hacked(Core.TeamID team, NetworkBool unlocked) => Debug.Log($"[Objective:{objectiveName}] hacked by {team}" + (unlocked ? " (unlocked)" : " (progress bonus)"));

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Respawned() => Debug.Log($"[Objective:{objectiveName}] is available again");

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
