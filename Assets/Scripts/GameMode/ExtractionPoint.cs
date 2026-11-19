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

        [Header("The Run (v0.10)")]
        [Tooltip("The single central getaway: pays carried loot in full. Every other exit is a side exit and pays GameModeConfig.sideExitPayout.")]
        [SerializeField] private bool isCentral = false;
        [Tooltip("Overrides the payout fraction for this exit when > 0.")]
        [SerializeField] private float payoutOverride = 0f;

        /// <summary>(v0.10) Live registry on this peer.</summary>
        public static readonly List<ExtractionPoint> All = new List<ExtractionPoint>();

        public bool IsCentral => isCentral;

        /// <summary>(v0.10) Fraction of carried value banked here. Always 1 under the pre-quota rules.</summary>
        public float PayoutMultiplier
        {
            get
            {
                var gm = ExtractionHeistGameMode.Instance;
                if (gm == null || !gm.Config.UseRunRules) return 1f;
                if (payoutOverride > 0f) return payoutOverride;
                return isCentral ? 1f : gm.Config.sideExitPayout;
            }
        }

        [Networked] public NetworkBool IsActive { get; set; }
        /// <summary>(v0.18) Which way the lift's doors face, in degrees. Chosen once by the state authority.</summary>
        [Networked] public float DoorYaw { get; set; }
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
            if (!All.Contains(this)) All.Add(this);
            ExtractionHeistGameMode.Instance?.RegisterExtractionPoint(this);

            if (Object.HasStateAuthority)
            {
                IsActive = true;
                CurrentExtractingTeam = Core.TeamID.None;
                Progress = 0f;
                DoorYaw = PickDoorYaw(); // (v0.18)
            }
            BuildLift(); // (v0.18)
        }

        // ------------------------------------------------------------------
        // The lift (v0.18): every exit is a lift car whose doors open when the getaway is called
        //
        // The car is a picture only. It has no colliders and does not change the NavMesh, so it can never
        // trap a player when the doors close after a crew leaves, and bots walk to an exit exactly as before.
        // The zone is still the circle of extractionRadius around this object; the car stands in its centre.

        /// <summary>True while a crew could leave from here: the getaway has been called and this exit is not resetting.</summary>
        public bool IsOpen
        {
            get
            {
                var gm = ExtractionHeistGameMode.Instance;
                return Object != null && IsActive && gm != null && gm.Object != null && gm.ExtractionOpen
                       && gm.CurrentState != ExtractionHeistGameMode.GameState.MatchEnded;
            }
        }

        public string ExitName => gameObject.name;

        private const float LiftWidth = 3.4f, LiftDepth = 3.0f, DoorTravelSeconds = 0.9f;
        private Transform lift;
        private readonly Transform[] leaves = new Transform[4];
        private readonly float[] leafSide = new float[4];
        private readonly List<Renderer> lamps = new List<Renderer>();
        private Renderer cabinPanel;
        private Light cabinLight;
        private Art.ThreatOutline liftOutline;
        private float doorOpen;        // 0 shut, 1 open
        private int shownLamp = -1;
        private bool doorsKnown;

        /// <summary>
        /// The doors face along whichever of the exit's two axes has more open floor, so a car in a long room
        /// opens toward the length of the room and not into a wall.
        /// </summary>
        private float PickDoorYaw()
        {
            float baseYaw = transform.eulerAngles.y;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            float Free(Vector3 dir) => Physics.Raycast(eye, dir, out RaycastHit hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 40f;
            Vector3 forward = Quaternion.Euler(0f, baseYaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, baseYaw, 0f) * Vector3.right;
            float along = Free(forward) + Free(-forward);
            float across = Free(right) + Free(-right);
            return across > along + 0.5f ? baseYaw + 90f : baseYaw;
        }

        private void BuildLift()
        {
            lift = Art.Prim.Group(transform, "Lift");
            Vector3 s = transform.lossyScale;
            lift.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            lift.position = transform.position;
            lift.rotation = Quaternion.Euler(0f, DoorYaw, 0f);

            var steel = Art.ArtMaterials.Colour(new Color(0.2f, 0.21f, 0.25f));
            var dark = Art.ArtMaterials.Colour(new Color(0.11f, 0.11f, 0.14f));
            var brass = Art.ArtMaterials.Colour(new Color(0.86f, 0.66f, 0.22f));
            var hazard = Art.ArtMaterials.Colour(new Color(0.95f, 0.75f, 0.1f));
            var door = Art.ArtMaterials.Colour(new Color(0.7f, 0.72f, 0.77f));
            var glass = Art.ArtMaterials.Translucent(new Color(0.6f, 0.85f, 0.95f, 0.22f));
            float hw = LiftWidth * 0.5f, hd = LiftDepth * 0.5f;

            Art.Prim.Box(lift, "Pad", new Vector3(0f, 0.05f, 0f), new Vector3(LiftWidth, 0.08f, LiftDepth), dark);
            Art.Prim.Box(lift, "Roof", new Vector3(0f, 3.22f, 0f), new Vector3(LiftWidth + 0.3f, 0.22f, LiftDepth + 0.3f), steel);
            Art.Prim.Box(lift, "Roof Trim", new Vector3(0f, 3.08f, 0f), new Vector3(LiftWidth + 0.34f, 0.06f, LiftDepth + 0.34f), brass);
            cabinPanel = Art.Prim.Box(lift, "Cabin Light", new Vector3(0f, 3.04f, 0f), new Vector3(2.4f, 0.04f, 2f), dark).GetComponent<Renderer>();
            Art.Prim.Rod(lift, "Motor", new Vector3(0f, 3.5f, 0f), new Vector3(1.1f, 0.18f, 1.1f), dark);
            lamps.Add(Art.Prim.Ball(lift, "Beacon", new Vector3(0f, 3.85f, 0f), new Vector3(0.34f, 0.34f, 0.34f), dark).GetComponent<Renderer>());

            foreach (float x in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -1f, 1f })
                    Art.Prim.Box(lift, "Post", new Vector3(x * (hw - 0.12f), 1.55f, z * (hd - 0.12f)), new Vector3(0.24f, 3.1f, 0.24f), steel);
                Art.Prim.Box(lift, "Side Panel", new Vector3(x * hw, 0.62f, 0f), new Vector3(0.1f, 1.1f, LiftDepth - 0.48f), steel);
                Art.Prim.Box(lift, "Side Glass", new Vector3(x * hw, 2.1f, 0f), new Vector3(0.05f, 1.86f, LiftDepth - 0.48f), glass);
                Art.Prim.Box(lift, "Handrail", new Vector3(x * (hw - 0.14f), 1.05f, 0f), new Vector3(0.06f, 0.06f, LiftDepth - 0.6f), brass);
            }

            int leaf = 0;
            foreach (float z in new[] { -1f, 1f })
            {
                Art.Prim.Box(lift, "Threshold", new Vector3(0f, 0.095f, z * (hd - 0.15f)), new Vector3(LiftWidth - 0.5f, 0.02f, 0.3f), hazard);
                Art.Prim.Box(lift, "Header", new Vector3(0f, 2.83f, z * (hd - 0.1f)), new Vector3(LiftWidth - 0.24f, 0.5f, 0.14f), dark);
                lamps.Add(Art.Prim.Box(lift, "Door Lamp", new Vector3(0f, 2.83f, z * (hd - 0.01f)), new Vector3(1.8f, 0.14f, 0.05f), dark).GetComponent<Renderer>());
                foreach (float x in new[] { -1f, 1f })
                {
                    var go = Art.Prim.Box(lift, "Door", Vector3.zero, Vector3.one, door);
                    Art.Prim.Box(go.transform, "Door Stripe", new Vector3(-x * 0.42f, 0f, 0f), new Vector3(0.05f, 0.9f, 1.3f), brass);
                    leaves[leaf] = go.transform;
                    leafSide[leaf] = x;
                    go.transform.localPosition = new Vector3(0f, 1.3f, z * (hd - 0.1f));
                    leaf++;
                }
            }

            var lightGo = new GameObject("Cabin Glow");
            lightGo.transform.SetParent(lift, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            cabinLight = lightGo.AddComponent<Light>();
            cabinLight.type = LightType.Point;
            cabinLight.range = 8f;
            cabinLight.intensity = 0f;
            cabinLight.color = new Color(0.7f, 1f, 0.8f);
            cabinLight.shadows = LightShadows.None;

            PoseDoors(0f);
            liftOutline = Art.ThreatOutline.Attach(gameObject, Art.ThreatOutline.WayOut, always: true);
            liftOutline.Set(false, Art.ThreatOutline.WayOut);
        }

        /// <summary>Each leaf slides toward its own post and narrows as it goes, like a door running into its pocket.</summary>
        private void PoseDoors(float open)
        {
            float t = open * open * (3f - 2f * open);
            float half = (LiftWidth - 0.48f) * 0.5f;          // clear width between the posts, halved
            float width = Mathf.Lerp(half, 0.14f, t);
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                if (l == null) continue;
                float outer = leafSide[i] * half;              // the post this leaf runs to
                var p = l.localPosition;
                l.localPosition = new Vector3(outer - leafSide[i] * width * 0.5f, p.y, p.z);
                l.localScale = new Vector3(width, 2.56f, 0.07f);
            }
        }

        public override void Render()
        {
            if (lift == null) return;
            bool open = IsOpen;
            if (!doorsKnown)
            {
                // The first frame shows the state as it is: no opening animation for a peer that joins late.
                doorsKnown = true;
                doorOpen = open ? 1f : 0f;
                wasOpen = open;
                PoseDoors(doorOpen);
            }
            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(doorOpen, target))
            {
                doorOpen = Mathf.MoveTowards(doorOpen, target, Time.deltaTime / DoorTravelSeconds);
                PoseDoors(doorOpen);
            }
            if (open != wasOpen)
            {
                wasOpen = open;
                if (open)
                {
                    Art.Fx.Ring(transform.position + Vector3.up * 0.1f, extractionRadius, Art.ThreatOutline.WayOut, 0.9f, 0.18f);
                    Art.Fx.Flash(transform.position + Vector3.up * 2f, Art.ThreatOutline.WayOut, 14f, 4f, 0.6f);
                    // [v0.22] AudioSource.PlayClipAtPoint(Core.ProceduralSfx.LiftDing, transform.position + Vector3.up * 2f, 1f); // (v0.20)
                    Audio.Sfx.PlayAt(Core.ProceduralSfx.LiftDing, transform.position + Vector3.up * 2f, 1f, Audio.Sfx.Room); // (v0.23) heard in the room, not across the map
                }
            }

            // 0 shut, 1 open, 2 a crew is leaving, 3 contested
            int lamp = !open ? 0 : (CurrentExtractingTeam == Core.TeamID.None ? 1 : (IsContested ? 3 : 2));
            if (lamp != shownLamp)
            {
                shownLamp = lamp;
                Color c = lamp == 0 ? new Color(0.85f, 0.14f, 0.1f)
                        : lamp == 1 ? Art.ThreatOutline.WayOut
                        : lamp == 3 ? new Color(1f, 0.55f, 0.1f)
                        : (Core.TeamUtil.IsValid(CurrentExtractingTeam) ? Art.CrewStyle.Get(CurrentExtractingTeam).Primary : Art.ThreatOutline.WayOut);
                var glow = Art.ArtMaterials.Emissive(c);
                for (int i = 0; i < lamps.Count; i++) if (lamps[i] != null) lamps[i].sharedMaterial = glow;
                if (cabinPanel != null) cabinPanel.sharedMaterial = open ? Art.ArtMaterials.Emissive(new Color(0.85f, 1f, 0.9f)) : Art.ArtMaterials.Colour(new Color(0.11f, 0.11f, 0.14f));
                if (liftOutline != null) liftOutline.Set(open, lamp == 3 ? c : Art.ThreatOutline.WayOut);
            }
            if (cabinLight != null) cabinLight.intensity = Mathf.MoveTowards(cabinLight.intensity, open ? 2.2f : 0f, Time.deltaTime * 4f);
        }
        private bool wasOpen;

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
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

            // [v0.9] First eligible team present (deterministic order: lowest team id)
            // (v0.10) the scan starts at a different crew every tick, so no crew wins every simultaneous claim
            for (int step = 0; step < Core.GameConstants.MAX_TEAMS; step++)
            {
                int t = Core.MatchRules.RotatedIndex((int)Runner.Tick, step, Core.GameConstants.MAX_TEAMS);
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
            // [v0.9] gm.OnTeamExtractionCompleted(extractedTeam, extractingMembers);
            gm.OnTeamExtractionCompleted(extractedTeam, extractingMembers, PayoutMultiplier); // (v0.10)

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
