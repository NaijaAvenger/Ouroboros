using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Static security camera. While enabled it reveals any player inside its view cone (status flag
    /// <c>Revealed</c>) so guards and rival teams can see them on the map. Hackers, Saboteurs and EMPs
    /// disable it for a duration.
    /// </summary>
    // [v0.10] public class SecurityCamera : NetworkBehaviour, ISecurityDevice, IHackable
    public class SecurityCamera : NetworkBehaviour, ISecurityDevice, IHackable, IQuickHackable
    {
        [Tooltip("(v0.11) Seconds a quick hack blinds the camera for.")]
        [SerializeField] private float quickHackBlindSeconds = 20f;

        // ---- IQuickHackable (v0.11) ----
        public Vector3 HackPoint => transform.position;
        public float HackSeconds => 1.4f;
        public string HackPrompt(Network.NetworkPlayer by) => IsOff ? null : "Blind camera";
        public void CompleteHack(Network.NetworkPlayer by) => Disable(quickHackBlindSeconds);

        /// <summary>(v0.11) Disabled by a hack or EMP, or without power because of a blackout.</summary>
        public bool IsOff => IsDisabled || PowerNode.IsBlackedOut(transform.position);

        [SerializeField] private float viewRange = 15f;
        [SerializeField] private float viewAngle = 70f;
        [SerializeField] private float revealDuration = 2f;
        [SerializeField] private LayerMask occluders = ~0;
        [Tooltip("Players in stealth are not detected.")]
        [SerializeField] private bool respectStealth = true;

        [Header("Being seen (v0.22)")]
        [Tooltip("How many of the nearest guards are sent to where a player was seen.")]
        [SerializeField] private int guardsSent = 5;
        [Tooltip("While a player stays in view, guards are sent to their new position this often, in seconds.")]
        [SerializeField] private float resendSeconds = 3f;
        [Tooltip("A player seen again within this many seconds is not announced again, by any camera.")]
        [SerializeField] private float announceCooldown = 10f;

        /// <summary>(v0.22) Raised on every peer when a camera picks a player up: the camera, and who.</summary>
        public static event System.Action<SecurityCamera, Network.NetworkPlayer> Spotted;

        // State authority only: when each player was last announced (shared by all cameras) and last had guards sent by this one.
        private static readonly System.Collections.Generic.Dictionary<Network.NetworkPlayer, float> lastAnnounced = new System.Collections.Generic.Dictionary<Network.NetworkPlayer, float>();
        private readonly System.Collections.Generic.Dictionary<Network.NetworkPlayer, float> lastSent = new System.Collections.Generic.Dictionary<Network.NetworkPlayer, float>();

        [Networked] private TickTimer DisabledTimer { get; set; }
        [Networked] private NetworkBool Spotting { get; set; }

        private AI.VisionCone cone;

        public bool IsDisabled => DisabledTimer.IsRunning && !DisabledTimer.Expired(Runner);

        public override void Spawned()
        {
            // Ground cone: the camera is mounted high, so project its range onto the floor beneath it
            cone = AI.VisionCone.Attach(transform, viewRange, viewAngle, groundOffset: transform.position.y, AI.VisionCone.IdleColor);
            QuickHack.Register(this); // (v0.11)

            // (v0.14) a code-built camera in place of the scene's box
            var placeholder = GetComponent<Renderer>();
            if (placeholder != null) placeholder.enabled = false;
            // [v0.16] Art.GearBuilder.BuildCamera(transform, out lensRenderer);
            // (v0.17) half as big again, and outlined: a camera high on a wall was easy to miss
            var model = Art.GearBuilder.BuildCamera(transform, out lensRenderer);
            model.localScale *= 1.5f;
            outline = Art.ThreatOutline.Attach(gameObject, Art.ThreatOutline.Watcher);
        }

        private Art.ThreatOutline outline;
        private Renderer lensRenderer;
        private int shownLens = -1;

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            QuickHack.Unregister(this);
            if (cone != null) Destroy(cone.gameObject);
        }

        public override void Render()
        {
            if (cone == null) return;
            var alarm = GameMode.AlarmSystem.Instance;
            float scale = alarm != null && alarm.Object != null ? alarm.DetectionMultiplier : 1f;
            cone.Configure(viewRange * scale, viewAngle);
            // [v0.10] cone.SetColor(IsDisabled ? AI.VisionCone.DisabledColor : Spotting ? AI.VisionCone.AlertColor : AI.VisionCone.IdleColor);
            cone.SetColor(IsOff ? AI.VisionCone.DisabledColor : Spotting ? AI.VisionCone.AlertColor : AI.VisionCone.IdleColor);

            if (outline != null) outline.Set(!IsOff, Spotting ? Art.ThreatOutline.Hostile : Art.ThreatOutline.Watcher); // (v0.17)

            // (v0.14) the lens says the same thing as the cone: off, watching, or it has seen someone
            int lens = IsOff ? 0 : (Spotting ? 2 : 1);
            if (lensRenderer != null && lens != shownLens)
            {
                shownLens = lens;
                lensRenderer.sharedMaterial = Art.ArtMaterials.Emissive(lens == 0 ? new Color(0.25f, 0.25f, 0.28f) : lens == 2 ? new Color(1f, 0.15f, 0.1f) : new Color(0.2f, 0.9f, 1f));
            }
        }

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
            // [v0.10] if (!Object.HasStateAuthority || IsDisabled) return;
            if (!Object.HasStateAuthority) return;
            if (IsOff) { if (Spotting) Spotting = false; return; } // (v0.11) hacked, EMP'd or blacked out
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var alarm = GameMode.AlarmSystem.Instance;
            float range = viewRange * (alarm != null && alarm.Object != null ? alarm.DetectionMultiplier : 1f);
            var players = Network.NetworkPlayer.FindPlayersInRadius(transform.position, range, Core.TeamID.None, aliveOnly: true);
            int spotted = 0;
            foreach (var p in players)
            {
                if (respectStealth && p.HasStatus(Core.StatusFlags.Stealthed)) continue;
                if (!AI.Perception.CanSee(transform.position, transform.forward, p.EyePosition, range, viewAngle, occluders)) continue;
                p.ApplyTimedStatus(Core.StatusFlags.Revealed, revealDuration);
                AI.AIBlackboard.ReportSighting(p.transform.position, p, priority: 0.9f); // v0.6: guards converge on camera spots
                spotted++;
                OnPlayerSeen(p); // (v0.22)
            }
            Spotting = spotted > 0;
            if (spotted > 0)
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                float perSecond = gm != null ? gm.Config.alarmOnCameraSpotPerSecond : 8f;
                GameMode.AlarmSystem.Raise(perSecond * Runner.DeltaTime * Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS, name);
            }
        }

        /// <summary>
        /// (v0.22) What being on camera costs, beyond the alarm:
        ///  - every crew can see a marker over the player for as long as the camera has them, and a moment after;
        ///  - every crew is told who was seen and where, once per player every few seconds;
        ///  - the nearest guards are sent to the spot, and keep coming after the player has left the picture.
        /// </summary>
        private void OnPlayerSeen(Network.NetworkPlayer p)
        {
            // Renewed on every scan, so the marker lasts exactly as long as the camera's view of them.
            float scanSeconds = Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS * Runner.DeltaTime;
            p.MarkFor(Core.TeamID.None, Mathf.Max(0.6f, scanSeconds * 2.5f));

            float now = Time.time;
            if (!lastSent.TryGetValue(p, out float sent) || now - sent >= resendSeconds || now < sent)
            {
                lastSent[p] = now;
                AI.AIBlackboard.DispatchNearest(p.Position, guardsSent);
            }
            if (!lastAnnounced.TryGetValue(p, out float told) || now - told >= announceCooldown || now < told)
            {
                lastAnnounced[p] = now;
                RPC_Spotted(p.Object.Id);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Spotted(NetworkId who)
        {
            Network.NetworkPlayer player = null;
            if (Runner.TryFindObject(who, out NetworkObject obj) && obj != null) player = obj.GetComponent<Network.NetworkPlayer>();
            if (player != null) Spotted?.Invoke(this, player);
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
