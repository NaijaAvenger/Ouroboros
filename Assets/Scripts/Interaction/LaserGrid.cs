using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A wall of laser beams across a doorway or corridor (v0.14). Walk through it live and it burns, tags you
    /// for every guard and raises the alarm. It is a gear puzzle with several answers:
    ///
    ///  - an EMP (the Cipher's blast, the Spider's sabotage, or the EMP Emitter gadget) switches it off;
    ///  - a quick hack on its control box switches it off for longer;
    ///  - cutting the district's power (a power junction) switches it off for the blackout;
    ///  - a power slide goes under the lowest beam.
    ///
    /// The grid spans local X (<see cref="width"/>) and stands <see cref="height"/> tall; it is thin along local Z.
    /// </summary>
    public class LaserGrid : NetworkBehaviour, ISecurityDevice, IQuickHackable
    {
        public static readonly List<LaserGrid> All = new List<LaserGrid>();

        [SerializeField] private string gridName = "Laser Grid";
        [SerializeField] private float width = 4f;
        [SerializeField] private float height = 3f;
        [SerializeField] private float thickness = 0.6f;
        [Tooltip("Height of the lowest beam. A sliding player passes under it.")]
        [SerializeField] private float lowestBeam = 1.1f;
        [SerializeField] private int beams = 6;
        [SerializeField] private float damagePerSecond = 35f;
        [SerializeField] private float alarmOnTrip = 12f;
        [SerializeField] private float revealSeconds = 6f;
        [SerializeField] private float hackOffSeconds = 30f;
        [SerializeField] private float hackSeconds = 2f;
        [Tooltip("Where the control box is, relative to the grid. That is what a hack is aimed at.")]
        [SerializeField] private Vector3 controlOffset = new Vector3(2.4f, 1.3f, 0f);

        [Networked] private TickTimer DisabledTimer { get; set; }
        [Networked] private TickTimer TripCooldown { get; set; }

        /// <summary>Raised on every peer when someone trips a live grid (grid, who). Cosmetic.</summary>
        public static event System.Action<LaserGrid, Network.NetworkPlayer> Tripped;

        public string GridName => gridName;
        public bool IsDisabled => Object != null && DisabledTimer.IsRunning && !DisabledTimer.Expired(Runner);
        /// <summary>Off for any reason: disabled, or the district's power is cut.</summary>
        public bool IsOff => IsDisabled || PowerNode.IsBlackedOut(transform.position);

        // ---- IQuickHackable ----
        public Vector3 HackPoint => transform.TransformPoint(controlOffset);
        public float HackSeconds => hackSeconds;
        public string HackPrompt(Network.NetworkPlayer by) => IsOff ? null : "Kill the lasers";
        public void CompleteHack(Network.NetworkPlayer by) => Disable(hackOffSeconds);

        private GameObject beamRoot;
        private Renderer controlLight;
        private bool shownOff, shownOnce;

        public void Disable(float duration)
        {
            if (!Object.HasStateAuthority || duration <= 0f) return;
            float remaining = DisabledTimer.RemainingTime(Runner) ?? 0f;
            if (duration > remaining) DisabledTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }

        public override void Spawned()
        {
            All.Add(this);
            QuickHack.Register(this);
            BuildVisuals();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            QuickHack.Unregister(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || IsOff) return;

            var players = Network.NetworkPlayer.All;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;

                Vector3 local = transform.InverseTransformPoint(p.Position);
                if (Mathf.Abs(local.x) > width * 0.5f || Mathf.Abs(local.z) > thickness * 0.5f + 0.35f) continue;
                if (local.y < -0.5f || local.y > height) continue;
                if (p.HasStatus(Core.StatusFlags.Sliding) && lowestBeam >= 0.9f) continue; // under the bottom beam

                using (Combat.DamageContext.Begin(source: "Laser Grid", means: gridName))
                {
                    p.ApplyDamage(damagePerSecond * Runner.DeltaTime, null, transform.position);
                }

                if (TripCooldown.ExpiredOrNotRunning(Runner))
                {
                    TripCooldown = TickTimer.CreateFromSeconds(Runner, 4f);
                    p.ApplyTimedStatus(Core.StatusFlags.Revealed, revealSeconds);
                    GameMode.AlarmSystem.Raise(alarmOnTrip, gridName);
                    AI.AIBlackboard.ReportNoise(transform.position, 30f);
                    RPC_Tripped(p.Object.Id);
                }
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Tripped(NetworkId who)
        {
            Network.NetworkPlayer player = null;
            if (Runner != null && Runner.TryFindObject(who, out var obj) && obj != null) player = obj.GetComponent<Network.NetworkPlayer>();
            Art.Fx.Flash(transform.position + Vector3.up * (height * 0.5f), new Color(1f, 0.15f, 0.1f), 14f, 5f, 0.5f);
            Tripped?.Invoke(this, player);
        }

        public override void Render()
        {
            bool off = IsOff;
            if (shownOnce && off == shownOff) return;
            shownOnce = true;
            shownOff = off;
            if (beamRoot != null) beamRoot.SetActive(!off);
            if (controlLight != null) controlLight.sharedMaterial = Art.ArtMaterials.Emissive(off ? new Color(0.3f, 1f, 0.5f) : new Color(1f, 0.15f, 0.1f));
        }

        private void BuildVisuals()
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // the scene's placeholder
            var root = Art.Prim.Group(transform, "Laser Model");
            Vector3 s = transform.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));

            var housing = Art.ArtMaterials.Colour(new Color(0.14f, 0.14f, 0.16f));
            foreach (float side in new[] { -1f, 1f })
            {
                Art.Prim.Box(root, "Emitter Post", new Vector3(side * (width * 0.5f + 0.08f), height * 0.5f, 0f), new Vector3(0.16f, height, 0.22f), housing);
            }

            beamRoot = Art.Prim.Group(root, "Beams").gameObject;
            var beam = Art.ArtMaterials.Emissive(new Color(1f, 0.1f, 0.08f));
            var haze = Art.ArtMaterials.Translucent(new Color(1f, 0.1f, 0.08f, 0.09f));
            int count = Mathf.Max(2, beams);
            for (int i = 0; i < count; i++)
            {
                float y = Mathf.Lerp(lowestBeam, height - 0.2f, i / (float)(count - 1));
                Art.Prim.Box(beamRoot.transform, "Beam", new Vector3(0f, y, 0f), new Vector3(width, 0.035f, 0.035f), beam);
            }
            // A faint red sheet from the lowest beam up: the gap underneath is visible, and so is the danger.
            Art.Prim.Box(beamRoot.transform, "Haze", new Vector3(0f, (lowestBeam + height - 0.2f) * 0.5f, 0f), new Vector3(width, height - 0.2f - lowestBeam, 0.02f), haze);

            var box = Art.Prim.Group(root, "Control Box", controlOffset);
            Art.Prim.Box(box, "Box", Vector3.zero, new Vector3(0.4f, 0.55f, 0.25f), housing);
            controlLight = Art.Prim.Box(box, "Light", new Vector3(0f, 0.12f, 0.13f), new Vector3(0.16f, 0.1f, 0.02f), Art.ArtMaterials.Emissive(new Color(1f, 0.15f, 0.1f))).GetComponent<Renderer>();
            shownOnce = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, thickness));
        }
    }
}
