using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A volume whose gravity can be switched off (v0.12). This component sits on the control box that is
    /// quick-hacked; the volume itself is <see cref="zoneCenter"/> / <see cref="zoneSize"/> in world space.
    /// While gravity is off, players inside float: they fly where they look, thrust up with Jump and down
    /// with Slide, and keep their momentum (<c>Player.PlayerController</c>). Guards keep their boots on the
    /// floor. Gravity returns on its own after a while, or when someone hacks the control again, and
    /// everyone in the air falls.
    /// </summary>
    public class GravityZone : NetworkBehaviour, IQuickHackable
    {
        public static readonly List<GravityZone> All = new List<GravityZone>();

        [SerializeField] private string zoneName = "Gravity Control";
        [SerializeField] private Vector3 zoneCenter;
        [SerializeField] private Vector3 zoneSize = new Vector3(20f, 10f, 20f);
        [SerializeField] private float zeroGSeconds = 30f;
        [Tooltip("Gravity is off here for the whole match.")]
        [SerializeField] private bool alwaysZeroG = false;
        [Tooltip("Translucent volume shown while gravity is off.")]
        [SerializeField] private GameObject zoneVisual;
        [Tooltip("Renderer on the control box, tinted by state.")]
        [SerializeField] private Renderer indicator;

        [Networked] private TickTimer ZeroGTimer { get; set; }

        /// <summary>Fired on every peer when a zone's gravity is switched (zone, crew, gravity now off).</summary>
        public static event System.Action<GravityZone, Core.TeamID, bool> Switched;

        public string ZoneName => zoneName;
        public Bounds Bounds => new Bounds(zoneCenter, zoneSize);
        public bool IsZeroG => Object != null && (alwaysZeroG || (ZeroGTimer.IsRunning && !ZeroGTimer.Expired(Runner)));

        private MaterialPropertyBlock block;
        private int shownState = -1;

        /// <summary>True when <paramref name="position"/> is inside a zone whose gravity is currently off.</summary>
        public static bool IsZeroGAt(Vector3 position)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var zone = All[i];
                if (zone != null && zone.IsZeroG && zone.Bounds.Contains(position)) return true;
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
        public float HackSeconds => 2f;
        public string HackPrompt(Network.NetworkPlayer by) => alwaysZeroG ? null : (IsZeroG ? "Restore gravity" : "Kill gravity");

        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority || alwaysZeroG) return;
            bool off = !IsZeroG;
            ZeroGTimer = off ? TickTimer.CreateFromSeconds(Runner, zeroGSeconds) : TickTimer.None;
            RPC_Switched(by != null ? by.Team : Core.TeamID.None, off);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Switched(Core.TeamID team, NetworkBool gravityOff)
        {
            Debug.Log($"[GravityZone] {zoneName}: gravity {(gravityOff ? "OFF" : "restored")} by {Core.TeamUtil.DisplayName(team)}");
            Switched?.Invoke(this, team, gravityOff);
        }

        public override void Render()
        {
            int state = IsZeroG ? 1 : 0;
            if (state == shownState) return;
            shownState = state;
            if (zoneVisual != null) zoneVisual.SetActive(state == 1);
            if (indicator != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                Color c = state == 1 ? new Color(0.75f, 0.4f, 1f) : new Color(0.35f, 0.35f, 0.45f);
                block.SetColor("_Color", c); block.SetColor("_BaseColor", c);
                indicator.SetPropertyBlock(block);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.75f, 0.4f, 1f, 0.5f);
            Gizmos.DrawWireCube(zoneCenter, zoneSize);
        }
    }
}
