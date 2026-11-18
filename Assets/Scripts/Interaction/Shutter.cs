using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A roll-down shutter across a doorway (v0.11, milestone M3). Any player can quick-hack it shut or open:
    /// close it behind you to stall a chase, or open the one a rival just closed. It moves back to its resting
    /// state on its own after a while, and it blocks guards' paths while it is down.
    /// </summary>
    public class Shutter : NetworkBehaviour, IQuickHackable
    {
        [SerializeField] private string shutterName = "Shutter";
        [SerializeField] private GameObject barrier;
        [SerializeField] private UnityEngine.AI.NavMeshObstacle navObstacle;
        [Tooltip("State the shutter starts in and returns to.")]
        [SerializeField] private bool restsClosed = false;
        [Tooltip("Seconds a hacked state lasts before the shutter returns to rest. 0 = stays until hacked again.")]
        [SerializeField] private float holdSeconds = 25f;

        [Networked] public NetworkBool IsClosed { get; set; }
        [Networked] private TickTimer HoldTimer { get; set; }

        /// <summary>Fired on every peer when a shutter is hacked (shutter, crew, now closed).</summary>
        public static event System.Action<Shutter, Core.TeamID, bool> Toggled;

        public string ShutterName => shutterName;

        public override void Spawned()
        {
            QuickHack.Register(this);
            if (Object.HasStateAuthority) IsClosed = restsClosed;
            ApplyVisual();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            QuickHack.Unregister(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            if (HoldTimer.IsRunning && HoldTimer.Expired(Runner))
            {
                HoldTimer = TickTimer.None;
                IsClosed = restsClosed;
            }
        }

        public override void Render()
        {
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            bool closed = Object != null && IsClosed;
            if (barrier != null && barrier.activeSelf != closed) barrier.SetActive(closed);
            if (navObstacle == null && barrier != null) navObstacle = barrier.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (navObstacle != null && navObstacle.enabled != closed) navObstacle.enabled = closed;
        }

        // ---- IQuickHackable ----
        // The barrier is switched off while open, so aim at the frame (this object), not the barrier.
        public Vector3 HackPoint => transform.position;
        public float HackSeconds => 1.2f;
        public string HackPrompt(Network.NetworkPlayer by) => IsClosed ? "Open shutter" : "Close shutter";

        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority) return;
            IsClosed = !IsClosed;
            HoldTimer = holdSeconds > 0f && IsClosed != restsClosed ? TickTimer.CreateFromSeconds(Runner, holdSeconds) : TickTimer.None;
            RPC_Toggled(by != null ? by.Team : Core.TeamID.None, IsClosed);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Toggled(Core.TeamID team, NetworkBool closed)
        {
            Toggled?.Invoke(this, team, closed);
        }
    }
}
