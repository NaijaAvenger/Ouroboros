using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Static security camera. While enabled it reveals any player inside its view cone (status flag
    /// <c>Revealed</c>) so guards and rival teams can see them on the map. Hackers, Saboteurs and EMPs
    /// disable it for a duration.
    /// </summary>
    public class SecurityCamera : NetworkBehaviour, ISecurityDevice, IHackable
    {
        [SerializeField] private float viewRange = 15f;
        [SerializeField] private float viewAngle = 70f;
        [SerializeField] private float revealDuration = 2f;
        [SerializeField] private LayerMask occluders = ~0;
        [Tooltip("Players in stealth are not detected.")]
        [SerializeField] private bool respectStealth = true;

        [Networked] private TickTimer DisabledTimer { get; set; }

        public bool IsDisabled => DisabledTimer.IsRunning && !DisabledTimer.Expired(Runner);

        public void Disable(float duration)
        {
            if (!Object.HasStateAuthority) return;
            float remaining = DisabledTimer.RemainingTime(Runner) ?? 0f;
            if (duration > remaining)
            {
                DisabledTimer = TickTimer.CreateFromSeconds(Runner, duration);
                RPC_Disabled(duration);
            }
        }

        public void OnHacked(Core.TeamID byTeam, float duration)
        {
            Disable(duration);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || IsDisabled) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var players = Network.NetworkPlayer.FindPlayersInRadius(transform.position, viewRange, Core.TeamID.None, aliveOnly: true);
            foreach (var p in players)
            {
                if (respectStealth && p.HasStatus(Core.StatusFlags.Stealthed)) continue;
                if (!AI.Perception.CanSee(transform.position, transform.forward, p.EyePosition, viewRange, viewAngle, occluders)) continue;
                p.ApplyTimedStatus(Core.StatusFlags.Revealed, revealDuration);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Disabled(float duration)
        {
            Debug.Log($"[SecurityCamera] {name} disabled for {duration:0}s");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * transform.forward;
            Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position, left * viewRange);
            Gizmos.DrawRay(transform.position, right * viewRange);
        }
    }
}
