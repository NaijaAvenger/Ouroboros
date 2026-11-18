using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A district's power junction (v0.11, milestone M3). Quick-hack it to cut the power for a while: every
    /// camera and sentry turret within <see cref="radius"/> goes dark, guards inside see half as far, and the
    /// lights assigned to it go out. Everyone can see that it happened, and nobody can do it again until the
    /// node has recharged.
    /// </summary>
    public class PowerNode : NetworkBehaviour, IQuickHackable
    {
        public static readonly List<PowerNode> All = new List<PowerNode>();

        [SerializeField] private string nodeName = "Power Node";
        [SerializeField] private float radius = 28f;
        [SerializeField] private float blackoutSeconds = 20f;
        [SerializeField] private float rechargeSeconds = 45f;
        [Tooltip("Lights switched off during the blackout (cosmetic).")]
        [SerializeField] private Light[] lights;
        [Tooltip("Renderer tinted to show the node's state.")]
        [SerializeField] private Renderer indicator;

        [Networked] private TickTimer BlackoutTimer { get; set; }
        [Networked] private TickTimer RechargeTimer { get; set; }

        /// <summary>Fired on every peer when a node's power is cut (node, crew that did it).</summary>
        public static event System.Action<PowerNode, Core.TeamID> PowerCut;

        public string NodeName => nodeName;
        public float Radius => radius;
        public bool IsBlackout => Object != null && BlackoutTimer.IsRunning && !BlackoutTimer.Expired(Runner);
        public bool IsRecharging => Object != null && RechargeTimer.IsRunning && !RechargeTimer.Expired(Runner);

        private MaterialPropertyBlock block;
        private int shownState = -1;

        /// <summary>True when <paramref name="position"/> is inside a node's area while its power is cut.</summary>
        public static bool IsBlackedOut(Vector3 position)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var node = All[i];
                if (node == null || !node.IsBlackout) continue;
                if ((node.transform.position - position).sqrMagnitude <= node.radius * node.radius) return true;
            }
            return false;
        }

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            QuickHack.Register(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            QuickHack.Unregister(this);
        }

        // ---- IQuickHackable ----
        public Vector3 HackPoint => transform.position;
        public float HackSeconds => 2.5f;
        public string HackPrompt(Network.NetworkPlayer by) => IsBlackout || IsRecharging ? null : "Cut the power";

        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority || IsBlackout || IsRecharging) return;
            BlackoutTimer = TickTimer.CreateFromSeconds(Runner, blackoutSeconds);
            RechargeTimer = TickTimer.CreateFromSeconds(Runner, blackoutSeconds + rechargeSeconds);
            RPC_PowerCut(by != null ? by.Team : Core.TeamID.None);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PowerCut(Core.TeamID team)
        {
            Debug.Log($"[PowerNode] {nodeName}: power cut by {Core.TeamUtil.DisplayName(team)} for {blackoutSeconds:0}s");
            PowerCut?.Invoke(this, team);
        }

        public override void Render()
        {
            int state = IsBlackout ? 2 : (IsRecharging ? 1 : 0);
            if (state == shownState) return;
            shownState = state;

            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++) if (lights[i] != null) lights[i].enabled = state != 2;
            }
            if (indicator != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                Color c = state == 2 ? new Color(0.08f, 0.08f, 0.1f) : (state == 1 ? new Color(0.9f, 0.5f, 0.1f) : new Color(0.3f, 1f, 0.5f));
                block.SetColor("_Color", c); block.SetColor("_BaseColor", c);
                indicator.SetPropertyBlock(block);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
