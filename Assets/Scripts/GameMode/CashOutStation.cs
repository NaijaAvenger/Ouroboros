using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    /// <summary>What just happened at a station. Sent to every peer for the feed, the banner and the sound.</summary>
    public enum CashOutMoment { Started = 0, Added = 1, Hijacked = 2, Banked = 3 }

    /// <summary>
    /// A cash-out station (v0.16): where carried loot becomes banked money in the middle of a match.
    ///
    ///  - Stand at an idle station carrying loot and hold Interact: everything you carry goes into the pot and
    ///    a timer starts. When it runs out, the pot is banked for your crew. You do not leave the match.
    ///  - Everyone is told, with the place and the amount, and the station lights up in your crew's colour.
    ///    The alarm rises and guards come to look.
    ///  - Crewmates can add to a running cash-out, which lengthens it a little.
    ///  - A rival who holds Interact at the station hijacks it: the pot and what is left of the timer become
    ///    theirs. A hijack cannot progress while one of the owners stands at the station, and the countdown is
    ///    frozen while a hijack is under way. A Cipher hijacks faster.
    ///
    /// So banking is a fight to hold a public place for a minute, and it can be won by the crew that never
    /// opened a vault. State authority runs all of it; the numbers are in <c>Core.MatchRules</c>.
    /// </summary>
    public class CashOutStation : NetworkBehaviour
    {
        public static readonly List<CashOutStation> All = new List<CashOutStation>();

        [SerializeField] private string stationName = "Cash-Out";
        [SerializeField] private float interactRadius = 3f;
        [Tooltip("Seconds a cash-out takes from the first deposit.")]
        [SerializeField] private float cashOutSeconds = 60f;
        [Tooltip("Seconds added when the owners deposit more. The timer never goes past a full one.")]
        [SerializeField] private float extendSeconds = 10f;
        [Tooltip("Seconds of holding Interact to put loot in.")]
        [SerializeField] private float depositSeconds = 1f;
        [Tooltip("Seconds of holding Interact for a rival to take the cash-out over.")]
        [SerializeField] private float hijackSeconds = 4.5f;
        [Tooltip("A hijacked cash-out has at least this long left.")]
        [SerializeField] private float hijackFloorSeconds = 18f;
        [Tooltip("How fast an abandoned hijack drains, as a fraction per second.")]
        [SerializeField] private float hijackDrainPerSecond = 0.5f;
        [Tooltip("A Cipher's hijack time is multiplied by this.")]
        [SerializeField] private float cipherHijackMultiplier = 0.6f;
        [SerializeField] private float alarmOnStart = 8f;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] public int Pot { get; set; }
        [Networked] public float Remaining { get; set; }
        /// <summary>0 to 1: how far a rival's hijack has got.</summary>
        [Networked] public float HijackProgress { get; set; }
        [Networked] public Core.TeamID HijackTeam { get; set; }
        [Networked] public NetworkBool Contested { get; set; }
        [Networked] private float DepositProgress { get; set; }

        /// <summary>Raised on every peer: (station, what happened, the crew it concerns, the crew it was taken from, value).</summary>
        public static event System.Action<CashOutStation, CashOutMoment, Core.TeamID, Core.TeamID, int> Moment;

        public string StationName => stationName;
        public float InteractRadius => interactRadius;
        public float CashOutSeconds => cashOutSeconds;
        public bool IsActive => Object != null && OwnerTeam != Core.TeamID.None && Pot > 0;
        public bool IsBeingHijacked => IsActive && HijackTeam != Core.TeamID.None && HijackProgress > 0f;
        /// <summary>0 to 1: how far the cash-out has got.</summary>
        public float Progress => IsActive && cashOutSeconds > 0f ? Mathf.Clamp01(1f - Remaining / cashOutSeconds) : 0f;

        /// <summary>True if <paramref name="player"/> could put loot in right now.</summary>
        public bool CanDeposit(Network.NetworkPlayer player)
        {
            // [v0.16 first draft] a player carrying only the case could not deposit it:
            // return player != null && Object != null && player.IsActiveInMatch && player.CarriedLoot > 0
            //        && (!IsActive || OwnerTeam == player.Team);
            return player != null && Object != null && player.IsActiveInMatch && (player.CarriedLoot > 0 || CarriesCase(player))
                   && (!IsActive || OwnerTeam == player.Team);
        }

        /// <summary>True if <paramref name="player"/> is carrying a loot case that has not been banked.</summary>
        public static bool CarriesCase(Network.NetworkPlayer player)
        {
            if (player == null || player.Object == null) return false;
            for (int c = 0; c < LootCase.All.Count; c++)
            {
                var lootCase = LootCase.All[c];
                if (lootCase != null && lootCase.Object != null && !lootCase.Banked && lootCase.Carrier == player.PlayerRef) return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="player"/> could start or continue a hijack right now.</summary>
        public bool CanHijack(Network.NetworkPlayer player)
        {
            return player != null && IsActive && player.IsActiveInMatch && player.Team != OwnerTeam && Core.TeamUtil.IsValid(player.Team);
        }

        public bool InRange(Vector3 position)
        {
            Vector3 d = position - transform.position;
            return d.x * d.x + d.z * d.z <= interactRadius * interactRadius && Mathf.Abs(d.y) < 3f;
        }

        // ---- visuals ----
        private Renderer screen, beacon;
        private Renderer[] lamps;
        private GameObject column;
        private Material columnMaterial;
        private Core.TeamID shownOwner = (Core.TeamID)(-1);
        private int shownLamps = -1;
        private bool shownHijack;
        private readonly List<Network.NetworkPlayer> depositors = new List<Network.NetworkPlayer>();
        private readonly List<int> itemScratch = new List<int>();

        public override void Spawned()
        {
            All.Add(this);
            if (Object.HasStateAuthority) ResetStation();
            BuildModel();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            if (columnMaterial != null) Destroy(columnMaterial);
        }

        private void ResetStation()
        {
            OwnerTeam = Core.TeamID.None;
            Pot = 0;
            Remaining = 0f;
            HijackProgress = 0f;
            HijackTeam = Core.TeamID.None;
            Contested = false;
            DepositProgress = 0f;
        }

        // ------------------------------------------------------------------
        // Simulation (state authority)

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            var gm = ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null) return;
            if (!gm.IsMatchLive)
            {
                // A new match starts clean. A cash-out still running when a match ends is not banked.
                if (IsActive || DepositProgress > 0f) ResetStation();
                return;
            }

            float dt = Runner.DeltaTime;
            depositors.Clear();
            bool ownerPresent = false, rivalWorking = false;
            Network.NetworkPlayer thief = null;

            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch || !InRange(p.Position)) continue;
                bool working = p.HasStatus(Core.StatusFlags.Interacting) && !p.HasStatus(Core.StatusFlags.EMPDisabled);

                if (IsActive && p.Team == OwnerTeam) ownerPresent = true;
                if (working && CanDeposit(p)) depositors.Add(p);
                else if (working && CanHijack(p) && (HijackTeam == Core.TeamID.None || HijackTeam == p.Team || HijackProgress <= 0f))
                {
                    rivalWorking = true;
                    if (thief == null || p.ClassType == Core.PlayerClassType.Hacker) thief = p;
                }
            }

            // ---- deposits
            if (depositors.Count > 0)
            {
                DepositProgress += dt;
                // [v0.20] if (DepositProgress >= depositSeconds)
                if (DepositProgress >= depositSeconds * Core.VaultRules.ActionTime(ExtractionHeistGameMode.PerksOf(depositors[0].Team))) // (v0.21) Quick Hands
                {
                    DepositProgress = 0f;
                    for (int i = 0; i < depositors.Count; i++) Deposit(depositors[i], gm);
                }
            }
            else if (DepositProgress > 0f) DepositProgress = 0f;

            if (!IsActive) return;

            // ---- hijack
            if (rivalWorking && thief != null)
            {
                if (HijackProgress <= 0f) HijackTeam = thief.Team;
                float seconds = hijackSeconds * (thief.ClassType == Core.PlayerClassType.Hacker ? Mathf.Clamp(cipherHijackMultiplier, 0.2f, 1f) : 1f);
                seconds *= Core.VaultRules.ActionTime(ExtractionHeistGameMode.PerksOf(thief.Team)); // (v0.21) Quick Hands
                HijackProgress = Core.MatchRules.HijackStep(HijackProgress, true, ownerPresent, dt, seconds, hijackDrainPerSecond);
                Contested = ownerPresent;
                if (HijackProgress >= 1f)
                {
                    var from = OwnerTeam;
                    OwnerTeam = thief.Team;
                    Remaining = Core.MatchRules.CashOutTimeAfterHijack(Remaining, hijackFloorSeconds);
                    HijackProgress = 0f;
                    HijackTeam = Core.TeamID.None;
                    Contested = false;
                    AlarmSystem.Raise(alarmOnStart * 0.5f, stationName);
                    RPC_Moment((byte)CashOutMoment.Hijacked, OwnerTeam, from, Pot);
                }
            }
            else
            {
                HijackProgress = Core.MatchRules.HijackStep(HijackProgress, false, ownerPresent, dt, hijackSeconds, hijackDrainPerSecond);
                Contested = false;
                if (HijackProgress <= 0f) HijackTeam = Core.TeamID.None;
            }

            // ---- the countdown, frozen while someone is in the middle of stealing it
            Remaining = Core.MatchRules.CashOutCountdown(Remaining, HijackProgress > 0f, dt);
            if (Remaining <= 0f)
            {
                var team = OwnerTeam;
                int banked = gm.BankCashOut(team, Pot);
                RPC_Moment((byte)CashOutMoment.Banked, team, Core.TeamID.None, banked);
                ResetStation();
            }
        }

        private void Deposit(Network.NetworkPlayer player, ExtractionHeistGameMode gm)
        {
            if (!CanDeposit(player)) return;
            itemScratch.Clear();
            int value = player.TakeAllLoot(itemScratch);

            // A carried case goes in with its carrier, at the case rate, exactly as it would at an exit.
            for (int c = LootCase.All.Count - 1; c >= 0; c--)
            {
                var lootCase = LootCase.All[c];
                if (lootCase != null && lootCase.Object != null) value += Mathf.RoundToInt(lootCase.BankFor(player) * gm.Config.pointsPerCaseLoot);
            }
            if (value <= 0) return;

            bool starting = !IsActive;
            Pot += value;
            if (starting)
            {
                OwnerTeam = player.Team;
                Remaining = cashOutSeconds;
                HijackProgress = 0f;
                HijackTeam = Core.TeamID.None;
                AlarmSystem.Raise(alarmOnStart, stationName);
                AI.AIBlackboard.ReportNoise(transform.position, 45f);
            }
            else
            {
                Remaining = Core.MatchRules.CashOutTimeAfterDeposit(Remaining, cashOutSeconds, extendSeconds);
            }
            RPC_Moment((byte)(starting ? CashOutMoment.Started : CashOutMoment.Added), player.Team, Core.TeamID.None, value);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Moment(byte moment, Core.TeamID team, Core.TeamID from, int value)
        {
            var what = (CashOutMoment)moment;
            Vector3 at = transform.position + Vector3.up * 1.2f;
            Color colour = Core.TeamUtil.IsValid(team) ? Art.CrewStyle.Get(team).Primary : Color.white;
            switch (what)
            {
                case CashOutMoment.Started:
                    Art.Fx.Ring(transform.position + Vector3.up * 0.08f, interactRadius * 2.5f, colour, 0.8f, 0.16f);
                    Art.Fx.Flash(at, colour, 14f, 4f, 0.5f);
                    break;
                case CashOutMoment.Added:
                    Art.Fx.Burst(at, new Color(1f, 0.85f, 0.3f, 1f), 14, 3f, 0.09f, 0.5f, 0.8f);
                    break;
                case CashOutMoment.Hijacked:
                    Art.Fx.Shock(at, interactRadius * 1.5f, colour, 0.5f);
                    break;
                case CashOutMoment.Banked:
                    Art.Fx.Burst(at + Vector3.up, new Color(1f, 0.85f, 0.3f, 1f), 40, 6f, 0.12f, 1.1f, 1.2f);
                    Art.Fx.Ring(transform.position + Vector3.up * 0.08f, interactRadius * 3f, new Color(1f, 0.85f, 0.3f, 0.9f), 1f, 0.2f);
                    Art.Fx.Flash(at, new Color(1f, 0.85f, 0.4f), 18f, 5f, 0.7f);
                    break;
            }
            Moment?.Invoke(this, what, team, from, value);
        }

        // ------------------------------------------------------------------
        // The model: a kiosk with a screen, a ring of lamps that fill as the cash-out runs, and a column of
        // light in the owner's colour that can be seen across a hall.

        private void BuildModel()
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // the scene's placeholder
            var root = Art.Prim.Group(transform, "Station Model");
            Vector3 s = transform.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));

            var body = Art.ArtMaterials.Colour(new Color(0.14f, 0.14f, 0.17f));
            var gold = Art.ArtMaterials.Colour(new Color(0.92f, 0.72f, 0.22f));
            Art.Prim.Box(root, "Plinth", new Vector3(0f, 0.1f, 0f), new Vector3(1.5f, 0.2f, 1.1f), gold);
            Art.Prim.Box(root, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(1.2f, 1.3f, 0.8f), body);
            Art.Prim.Box(root, "Head", new Vector3(0f, 1.75f, 0f), new Vector3(1.3f, 0.5f, 0.9f), body);
            Art.Prim.Box(root, "Trim", new Vector3(0f, 1.48f, 0f), new Vector3(1.34f, 0.05f, 0.94f), gold);
            Art.Prim.Box(root, "Tray", new Vector3(0f, 0.95f, 0.45f), new Vector3(0.8f, 0.06f, 0.3f), gold);
            Art.Prim.Box(root, "Slot", new Vector3(0f, 1.12f, 0.405f), new Vector3(0.6f, 0.06f, 0.02f), Art.ArtMaterials.Colour(Color.black));
            foreach (float side in new[] { -1f, 1f })
            {
                // A screen on both faces: the station can be worked and read from either side.
                var face = Art.Prim.Box(root, "Screen", new Vector3(0f, 1.75f, side * 0.455f), new Vector3(1.1f, 0.36f, 0.02f), Art.ArtMaterials.Emissive(Color.gray));
                if (side > 0f) screen = face.GetComponent<Renderer>(); else screenBack = face.GetComponent<Renderer>();
            }
            Art.Prim.Rod(root, "Mast", new Vector3(0f, 2.5f, 0f), new Vector3(0.05f, 0.5f, 0.05f), body);
            beacon = Art.Prim.Ball(root, "Beacon", new Vector3(0f, 3.05f, 0f), new Vector3(0.24f, 0.24f, 0.24f), Art.ArtMaterials.Emissive(Color.gray)).GetComponent<Renderer>();

            const int count = 12;
            lamps = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                lamps[i] = Art.Prim.Box(root, "Lamp", new Vector3(Mathf.Sin(a) * 0.62f, 2.08f, Mathf.Cos(a) * 0.42f), new Vector3(0.09f, 0.09f, 0.09f), Art.ArtMaterials.Colour(new Color(0.2f, 0.2f, 0.22f))).GetComponent<Renderer>();
            }

            // (v0.18) A coin with an arrow under it, turning and bobbing over the station: "money goes here".
            // Gold while the station is free, the owner's colour while a cash-out runs, red during a hijack.
            marker = Art.Prim.Group(root, "Marker", new Vector3(0f, MarkerHeight, 0f));
            var coinGold = Art.ArtMaterials.Emissive(Art.ThreatOutline.Objective);
            markerParts.Add(Art.Prim.Rod(marker, "Coin", Vector3.zero, new Vector3(0.7f, 0.04f, 0.7f), coinGold, new Vector3(90f, 0f, 0f)).GetComponent<Renderer>());
            Art.Prim.Rod(marker, "Coin Centre", Vector3.zero, new Vector3(0.42f, 0.055f, 0.42f), Art.ArtMaterials.Colour(new Color(0.55f, 0.38f, 0.08f)), new Vector3(90f, 0f, 0f));
            markerParts.Add(Art.Prim.Box(marker, "Coin Bar", Vector3.zero, new Vector3(0.09f, 0.3f, 0.13f), coinGold).GetComponent<Renderer>());
            foreach (float side in new[] { -1f, 1f })
            {
                // two strokes meeting at a point below the coin
                markerParts.Add(Art.Prim.Box(marker, "Arrow", new Vector3(side * 0.12f, -0.62f, 0f), new Vector3(0.1f, 0.4f, 0.1f), coinGold, new Vector3(0f, 0f, side * 35f)).GetComponent<Renderer>());
            }
            outline = Art.ThreatOutline.Attach(gameObject, Art.ThreatOutline.Objective, always: true);

            column = Art.Prim.Rod(root, "Light Column", new Vector3(0f, 8f, 0f), new Vector3(0.9f, 5f, 0.9f), null);
            columnMaterial = Art.ArtMaterials.TranslucentInstance(new Color(1f, 1f, 1f, 0.16f));
            column.GetComponent<Renderer>().sharedMaterial = columnMaterial;
            column.SetActive(false);
        }
        private Renderer screenBack;
        private int tickedSecond = -1;
        private const float MarkerHeight = 3.8f;
        private Transform marker;
        private readonly List<Renderer> markerParts = new List<Renderer>();
        private Art.ThreatOutline outline;

        public override void Render()
        {
            if (lamps == null) return;
            bool active = IsActive;
            bool hijack = IsBeingHijacked;
            Color crew = active && Core.TeamUtil.IsValid(OwnerTeam) ? Art.CrewStyle.Get(OwnerTeam).Primary : new Color(0.35f, 0.9f, 0.5f);

            // (v0.20) the last ten seconds tick, at the station: the sound of a cash-out about to land
            int second = active && !hijack && Remaining <= 10f ? Mathf.CeilToInt(Remaining) : -1;
            if (second != tickedSecond)
            {
                if (second > 0)
                    Audio.Sfx.PlayAt(Core.ProceduralSfx.CashTick, transform.position + Vector3.up * 1.5f, 0.9f, Audio.Sfx.Close); // [v0.22] AudioSource.PlayClipAtPoint
                tickedSecond = second;
            }

            // (v0.18) the marker bobs and turns all the time, so the station catches the eye even when idle
            if (marker != null)
            {
                marker.localPosition = new Vector3(0f, MarkerHeight + Mathf.Sin(Time.time * 2.2f) * 0.18f, 0f);
                marker.localRotation = Quaternion.Euler(0f, Time.time * 80f, 0f);
            }

            if (OwnerTeam != shownOwner || hijack != shownHijack)
            {
                shownOwner = OwnerTeam;
                shownHijack = hijack;
                shownLamps = -1;
                // (v0.18) marker and outline say whose it is: gold when free, a crew's colour, red when being stolen
                Color mark = hijack ? new Color(1f, 0.2f, 0.15f) : (active ? crew : Art.ThreatOutline.Objective);
                var markMaterial = Art.ArtMaterials.Emissive(mark);
                for (int i = 0; i < markerParts.Count; i++) if (markerParts[i] != null) markerParts[i].sharedMaterial = markMaterial;
                if (outline != null) outline.Set(true, mark);
                var face = Art.ArtMaterials.Emissive(hijack ? new Color(1f, 0.2f, 0.15f) : crew);
                if (screen != null) screen.sharedMaterial = face;
                if (screenBack != null) screenBack.sharedMaterial = face;
                if (beacon != null) beacon.sharedMaterial = face;
                if (column != null)
                {
                    column.SetActive(active);
                    columnMaterial.color = new Color(crew.r, crew.g, crew.b, 0.18f);
                }
            }

            // The beacon and the column pulse faster as the cash-out nears its end, and flicker during a hijack.
            if (active && column != null)
            {
                float rate = hijack ? 14f : Mathf.Lerp(2f, 7f, Progress);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate);
                var c = hijack ? new Color(1f, 0.2f, 0.15f) : crew;
                columnMaterial.color = new Color(c.r, c.g, c.b, 0.1f + 0.14f * pulse);
            }

            int lit = active ? Mathf.Clamp(Mathf.CeilToInt(Progress * lamps.Length), 0, lamps.Length) : 0;
            if (lit != shownLamps)
            {
                shownLamps = lit;
                var on = Art.ArtMaterials.Emissive(crew);
                var off = Art.ArtMaterials.Colour(new Color(0.2f, 0.2f, 0.22f));
                for (int i = 0; i < lamps.Length; i++) if (lamps[i] != null) lamps[i].sharedMaterial = i < lit ? on : off;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
