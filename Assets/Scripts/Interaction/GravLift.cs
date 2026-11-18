using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A lift pad (v0.11, milestone M3). Quick-hack it to switch it on for a while; anyone who steps on it while
    /// it runs is thrown up to the level above. A fast way onto a mezzanine or catwalk for whoever thought to
    /// start it, and a way to follow them for whoever did not.
    /// </summary>
    public class GravLift : NetworkBehaviour, IQuickHackable
    {
        [SerializeField] private string liftName = "Lift";
        [Tooltip("Height of the level this lift reaches, in metres above the pad.")]
        [SerializeField] private float liftHeight = 4f;
        [SerializeField] private float padRadius = 1.6f;
        [SerializeField] private float activeSeconds = 25f;
        [Tooltip("Runs without being hacked.")]
        [SerializeField] private bool alwaysOn = false;
        [Tooltip("Renderer tinted to show whether the lift is running.")]
        [SerializeField] private Renderer indicator;

        [Networked] private TickTimer ActiveTimer { get; set; }
        /// <summary>Short gap between launches so one player is not launched every tick.</summary>
        [Networked] private TickTimer RearmTimer { get; set; }

        public bool IsActive => Object != null && (alwaysOn || (ActiveTimer.IsRunning && !ActiveTimer.Expired(Runner)));
        public string LiftName => liftName;

        private MaterialPropertyBlock block;
        private int shownState = -1;

        public override void Spawned() { QuickHack.Register(this); }
        public override void Despawned(NetworkRunner runner, bool hasState) { QuickHack.Unregister(this); }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !IsActive) return;
            if (!RearmTimer.ExpiredOrNotRunning(Runner)) return;

            // Speed needed to rise liftHeight plus a little clearance under the player controller's gravity.
            float speed = Mathf.Sqrt(2f * 9.81f * (liftHeight + 1.2f));
            bool launched = false;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                Vector3 d = p.transform.position - transform.position;
                if (d.y < -0.5f || d.y > 1.2f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > padRadius * padRadius) continue;
                var controller = p.Controller;
                if (controller == null) continue;
                controller.Launch(speed);
                launched = true;
            }
            if (launched) RearmTimer = TickTimer.CreateFromSeconds(Runner, 0.5f);
        }

        // ---- IQuickHackable ----
        public Vector3 HackPoint => transform.position + Vector3.up * 0.3f;
        public float HackSeconds => 1f;
        public string HackPrompt(Network.NetworkPlayer by) => IsActive ? null : "Start lift";

        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority) return;
            ActiveTimer = TickTimer.CreateFromSeconds(Runner, activeSeconds);
        }

        public override void Render()
        {
            int state = IsActive ? 1 : 0;
            if (state == shownState || indicator == null) return;
            shownState = state;
            if (block == null) block = new MaterialPropertyBlock();
            Color c = state == 1 ? new Color(0.3f, 0.9f, 1f) : new Color(0.2f, 0.25f, 0.3f);
            block.SetColor("_Color", c); block.SetColor("_BaseColor", c);
            indicator.SetPropertyBlock(block);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, padRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * liftHeight);
        }
    }
}
