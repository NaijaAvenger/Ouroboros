using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Reinforced door. A Demolitions breaching charge opens it permanently; a Hacker's System Hack opens
    /// it temporarily. Any collider/renderer on <see cref="doorVisual"/> is toggled with the state so the
    /// door blocks movement while closed.
    /// </summary>
    public class BreachableDoor : NetworkBehaviour, IBreachable, IHackable
    {
        [SerializeField] private GameObject doorVisual;
        [Tooltip("(v0.6) Optional NavMeshObstacle (carving) that blocks guards while the door is closed.")]
        [SerializeField] private UnityEngine.AI.NavMeshObstacle navObstacle;
        [Tooltip("If set, only this team may hack it open (0 = any).")]
        [SerializeField] private Core.TeamID ownerTeam = Core.TeamID.None;

        [Networked] public NetworkBool IsBreached { get; set; }
        [Networked] private TickTimer HackOpenTimer { get; set; }

        bool IBreachable.IsBreached => IsBreached;
        public bool IsOpen => IsBreached || (HackOpenTimer.IsRunning && !HackOpenTimer.Expired(Runner));

        public override void Spawned()
        {
            ApplyVisual();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            if (HackOpenTimer.IsRunning && HackOpenTimer.Expired(Runner))
            {
                HackOpenTimer = TickTimer.None;
            }
        }

        public override void Render()
        {
            // Both the breach flag and the hack timer affect the visual; re-evaluate every frame cheaply.
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (doorVisual != null && doorVisual.activeSelf == IsOpen)
            {
                doorVisual.SetActive(!IsOpen);
            }
            if (navObstacle == null && doorVisual != null) navObstacle = doorVisual.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (navObstacle != null && navObstacle.enabled == IsOpen)
            {
                navObstacle.enabled = !IsOpen; // carved hole appears when closed, disappears when open
            }
        }

        public void Breach(Core.TeamID byTeam)
        {
            if (!Object.HasStateAuthority || IsBreached) return;
            IsBreached = true;
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            GameMode.AlarmSystem.Raise(gm != null ? gm.Config.alarmOnBreach : 30f, name);
            RPC_Breached(byTeam);
        }

        public void OnHacked(Core.TeamID byTeam, float duration)
        {
            if (!Object.HasStateAuthority || IsBreached) return;
            if (ownerTeam != Core.TeamID.None && ownerTeam != byTeam) return;
            HackOpenTimer = TickTimer.CreateFromSeconds(Runner, duration);
            RPC_Hacked(byTeam, duration);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Breached(Core.TeamID team) => Debug.Log($"[BreachableDoor] {name} breached by {team}");

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Hacked(Core.TeamID team, float duration) => Debug.Log($"[BreachableDoor] {name} hacked open by {team} for {duration:0}s");
    }
}
