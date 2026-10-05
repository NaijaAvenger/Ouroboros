using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Security terminal. A Hacker's System Hack activates it instantly; anyone else can hold Interact
    /// for <see cref="holdSeconds"/>. Activation unlocks / boosts its linked vaults for a while and lowers
    /// the alarm. Re-arms after <see cref="cooldownSeconds"/>.
    /// </summary>
    public class HackTerminal : NetworkBehaviour, IHackable
    {
        [SerializeField] private string terminalName = "Security Terminal";
        [SerializeField] private GameMode.LootObjective[] linkedObjectives;
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private float holdSeconds = 12f;
        [SerializeField] private float unlockDuration = 60f;
        [SerializeField] private float alarmReduction = 25f;
        [SerializeField] private float cooldownSeconds = 90f;

        [Networked] public NetworkBool IsReady { get; set; }
        [Networked] public float Progress { get; set; }
        [Networked] public Core.TeamID WorkingTeam { get; set; }
        [Networked] private TickTimer CooldownTimer { get; set; }

        public static readonly List<HackTerminal> All = new List<HackTerminal>();
        public static event System.Action<HackTerminal, Core.TeamID> Activated;

        public string TerminalName => terminalName;
        public float InteractRadius => interactRadius;
        public float ProgressNormalized => Mathf.Clamp01(Progress / Mathf.Max(0.01f, holdSeconds));

        private readonly List<Network.NetworkPlayer> workers = new List<Network.NetworkPlayer>();

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Object.HasStateAuthority) IsReady = true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (!IsReady)
            {
                if (CooldownTimer.IsRunning && CooldownTimer.Expired(Runner))
                {
                    CooldownTimer = TickTimer.None;
                    IsReady = true;
                }
                return;
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;

            workers.Clear();
            float rSq = interactRadius * interactRadius;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                if ((p.transform.position - transform.position).sqrMagnitude > rSq) continue;
                if (p.HasStatus(Core.StatusFlags.Interacting) && !p.HasStatus(Core.StatusFlags.EMPDisabled)) workers.Add(p);
            }

            if (workers.Count == 0)
            {
                if (Progress > 0f) Progress = Mathf.Max(0f, Progress - Runner.DeltaTime * 2f); // bleeds off
                if (Progress <= 0f) WorkingTeam = Core.TeamID.None;
                return;
            }

            var team = WorkingTeam != Core.TeamID.None ? WorkingTeam : workers[0].Team;
            bool contested = false;
            foreach (var w in workers) if (w.Team != team) contested = true;
            if (contested) return;

            WorkingTeam = team;
            Progress += Runner.DeltaTime;
            if (Progress >= holdSeconds) Activate(team);
        }

        public void OnHacked(Core.TeamID byTeam, float duration)
        {
            if (!Object.HasStateAuthority || !IsReady) return;
            Activate(byTeam);
        }

        private void Activate(Core.TeamID team)
        {
            IsReady = false;
            Progress = 0f;
            WorkingTeam = Core.TeamID.None;
            CooldownTimer = TickTimer.CreateFromSeconds(Runner, cooldownSeconds);

            if (linkedObjectives != null)
            {
                foreach (var obj in linkedObjectives)
                {
                    if (obj != null && obj.Object != null) obj.OnHacked(team, unlockDuration);
                }
            }
            GameMode.AlarmSystem.Raise(-alarmReduction, terminalName);
            RPC_Activated(team);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Activated(Core.TeamID team)
        {
            Debug.Log($"[HackTerminal] {terminalName} activated by {team}");
            Activated?.Invoke(this, team);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
            if (linkedObjectives == null) return;
            foreach (var o in linkedObjectives) if (o != null) Gizmos.DrawLine(transform.position, o.transform.position);
        }
    }
}
