using UnityEngine;
using Fusion;
using System.Collections.Generic;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Represents an extraction point where teams can successfully extract.
    ///
    /// v0.2: zone membership is computed every tick from the <see cref="Network.NetworkPlayer"/> registry
    /// instead of Unity trigger callbacks (which run on Unity physics time, not Fusion ticks, and never
    /// fire for players that die inside the zone). Extraction progress is a replicated float so enemies
    /// entering the zone can pause it ("contested") and UI can show a progress bar.
    /// </summary>
    public class ExtractionPoint : NetworkBehaviour
    {
        [Header("Extraction Configuration")]
        [Tooltip("Overrides GameModeConfig.extractionTime when > 0.")]
        [SerializeField] private float extractionTime = 0f;
        [SerializeField] private float extractionRadius = 5f;
        [Tooltip("Overrides GameModeConfig.requireWholeTeamForExtraction when set.")]
        [SerializeField] private bool requiresAllTeamMembers = false;
        [SerializeField] private bool useConfigForTeamRequirement = true;

        [Networked] public NetworkBool IsActive { get; set; }
        [Networked] public Core.TeamID CurrentExtractingTeam { get; set; }
        [Networked] public float Progress { get; set; }
        [Networked] public NetworkBool IsContested { get; set; }
        [Networked] public TickTimer ReactivationTimer { get; set; }
        // [v0.1] [Networked] public TickTimer ExtractionTimer { get; set; }  // replaced by pausable Progress

        // [v0.1] private Dictionary<PlayerRef, Core.TeamID> playersInZone = new Dictionary<PlayerRef, Core.TeamID>();
        private readonly List<Network.NetworkPlayer> playersInZone = new List<Network.NetworkPlayer>();
        private readonly List<Network.NetworkPlayer> extractingMembers = new List<Network.NetworkPlayer>();

        public float ExtractionTime
        {
            get
            {
                float baseTime = extractionTime > 0f ? extractionTime : (ExtractionHeistGameMode.Instance != null ? ExtractionHeistGameMode.Instance.Config.extractionTime : 10f);
                var alarm = AlarmSystem.Instance;
                return baseTime * (alarm != null && alarm.Object != null ? alarm.ExtractionTimeMultiplier : 1f); // v0.5: slower under lockdown
            }
        }

        public float ProgressNormalized => Mathf.Clamp01(Progress / Mathf.Max(0.01f, ExtractionTime));
        public float ExtractionRadius => extractionRadius;

        private bool RequiresWholeTeam
        {
            get
            {
                var gm = ExtractionHeistGameMode.Instance;
                if (useConfigForTeamRequirement && gm != null) return gm.Config.requireWholeTeamForExtraction;
                return requiresAllTeamMembers;
            }
        }

        public override void Spawned()
        {
            ExtractionHeistGameMode.Instance?.RegisterExtractionPoint(this);

            if (Object.HasStateAuthority)
            {
                IsActive = true;
                CurrentExtractingTeam = Core.TeamID.None;
                Progress = 0f;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ExtractionHeistGameMode.Instance?.UnregisterExtractionPoint(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            var gm = ExtractionHeistGameMode.Instance;
            if (gm == null || !gm.ExtractionOpen || gm.CurrentState == ExtractionHeistGameMode.GameState.MatchEnded)
            {
                if (CurrentExtractingTeam != Core.TeamID.None) CancelExtraction();
                return;
            }

            if (!IsActive)
            {
                if (ReactivationTimer.IsRunning && ReactivationTimer.Expired(Runner))
                {
                    ReactivationTimer = TickTimer.None;
                    IsActive = true;
                    RPC_PointReactivated();
                }
                return;
            }

            ScanZone(gm);

            if (CurrentExtractingTeam == Core.TeamID.None)
            {
                TryStartExtraction(gm);
                return;
            }

            // Count extracting team members and whether anyone else is present.
            int teamMembers = 0;
            bool enemyPresent = false;
            foreach (var p in playersInZone)
            {
                if (p.Team == CurrentExtractingTeam) teamMembers++;
                else enemyPresent = true;
            }

            if (teamMembers == 0)
            {
                CancelExtraction();
                return;
            }

            bool contested = enemyPresent && gm.Config.extractionContestable;
            if (contested != IsContested)
            {
                IsContested = contested;
                RPC_ContestChanged(contested);
            }
            if (contested) return;

            Progress += Runner.DeltaTime;
            if (Progress >= ExtractionTime)
            {
                CompleteExtraction(gm);
            }
        }

        // [v0.1] private void OnTriggerEnter(Collider other) { ... }   // Unity physics timing; replaced by ScanZone
        // [v0.1] private void OnTriggerExit(Collider other)  { ... }

        private void ScanZone(ExtractionHeistGameMode gm)
        {
            playersInZone.Clear();
            float radiusSq = extractionRadius * extractionRadius;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                if ((p.transform.position - transform.position).sqrMagnitude > radiusSq) continue;
                playersInZone.Add(p);
            }
        }

        private void TryStartExtraction(ExtractionHeistGameMode gm)
        {
            if (playersInZone.Count == 0) return;

            // First eligible team present (deterministic order: lowest team id)
            for (int t = 0; t < Core.GameConstants.MAX_TEAMS; t++)
            {
                Core.TeamID team = Core.TeamUtil.FromIndex(t);
                int eligible = 0;
                foreach (var p in playersInZone)
                {
                    if (p.Team == team && gm.CanExtract(p)) eligible++;
                }
                if (eligible == 0) continue;

                if (RequiresWholeTeam)
                {
                    var tm = Network.TeamManager.Instance;
                    var status = tm != null ? tm.GetTeamStatus(team) : default;
                    int stillInPlay = status.Alive + status.AwaitingRespawn;
                    if (eligible < stillInPlay) continue;
                }

                StartExtraction(team);
                return;
            }
        }

        private void StartExtraction(Core.TeamID team)
        {
            CurrentExtractingTeam = team;
            Progress = 0f;
            IsContested = false;
            // [v0.1] ExtractionTimer = TickTimer.CreateFromSeconds(Runner, extractionTime);
            RPC_ExtractionStarted(team);
        }

        private void CancelExtraction()
        {
            Core.TeamID previousTeam = CurrentExtractingTeam;
            CurrentExtractingTeam = Core.TeamID.None;
            Progress = 0f;
            IsContested = false;
            if (previousTeam != Core.TeamID.None) RPC_ExtractionCancelled(previousTeam);
        }

        private void CompleteExtraction(ExtractionHeistGameMode gm)
        {
            Core.TeamID extractedTeam = CurrentExtractingTeam;

            extractingMembers.Clear();
            foreach (var p in playersInZone)
            {
                if (p.Team == extractedTeam) extractingMembers.Add(p);
            }

            // [v0.1] var gameMode = FindObjectOfType<ExtractionHeistGameMode>(); gameMode.TeamExtractedSuccessfully(extractedTeam);
            gm.OnTeamExtractionCompleted(extractedTeam, extractingMembers);

            CurrentExtractingTeam = Core.TeamID.None;
            Progress = 0f;
            IsContested = false;

            float reactivation = gm.Config.extractionReactivationDelay;
            IsActive = false;
            if (reactivation > 0f)
            {
                ReactivationTimer = TickTimer.CreateFromSeconds(Runner, reactivation);
            }

            RPC_ExtractionCompleted(extractedTeam, extractingMembers.Count);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionStarted(Core.TeamID team)
        {
            Debug.Log($"[ExtractionPoint] {team} started extraction - {ExtractionTime}s required");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionCancelled(Core.TeamID team)
        {
            Debug.Log($"[ExtractionPoint] {team} extraction cancelled");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ContestChanged(NetworkBool contested)
        {
            Debug.Log(contested ? "[ExtractionPoint] Extraction contested!" : "[ExtractionPoint] Extraction resumed");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ExtractionCompleted(Core.TeamID team, int members)
        {
            Debug.Log($"[ExtractionPoint] {team} successfully extracted {members} member(s)!");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PointReactivated()
        {
            Debug.Log("[ExtractionPoint] Extraction point is open again");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, extractionRadius);
        }
    }
}
