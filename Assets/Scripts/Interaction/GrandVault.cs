using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>(v0.21) Things that happen at a Grand Vault that every crew is told about.</summary>
    public enum VaultMoment : byte { KeyDropped = 0, KeyTaken = 1, DrillStarted = 2, DrillStopped = 3, Opened = 4, PerkClaimed = 5 }

    /// <summary>
    /// A Grand Vault (v0.21): the main objective of a heist. It is a room, not a box: a strongroom behind a
    /// real vault door, holding the best loot on the map and one crew perk. There are four on a map, and
    /// emptying one is worth about a whole quota, so the shape of a match is "crack a vault, then get out".
    ///
    /// The door opens in one of up to three ways (<see cref="Core.VaultMethod"/>); each vault says which it allows:
    ///
    ///  - <b>Key.</b> The vault has guards. When the last of them dies, the key falls where they fell. Walk
    ///    over it to take it, hold Interact at the door to use it. Die with it and it falls again, for anyone.
    ///  - <b>Drill.</b> Hold Interact at the door to set the drill. It then runs by itself, loudly: every crew is
    ///    told, the alarm rises and guards come. It only runs while one of the crew that set it is near the
    ///    door, and runs backward while none is. A rival who reaches the door can pull the plug.
    ///  - <b>Crack.</b> A lock console beside the door (a <see cref="GameMode.LootObjective"/> pointing at this
    ///    vault) serves the sign and pulse puzzles. Quiet, and as fast as the player is good.
    ///
    /// A breaching charge through the room's weak wall, where it has one, is a fourth way in.
    ///
    /// When the vault opens its loot is laid out in the room, and the perk on the pedestal goes to the first
    /// crew to hold Interact at it. Everything here lives on the door's own object, beside its
    /// <see cref="VaultDoor"/>: the key, the drill and the perk are state, not separate network objects.
    /// </summary>
    [RequireComponent(typeof(VaultDoor))]
    public class GrandVault : NetworkBehaviour
    {
        public static readonly List<GrandVault> All = new List<GrandVault>();
        /// <summary>Raised on every peer: the vault, what happened, the crew it concerns, a second crew (the drill's owner when it is stopped), and the player.</summary>
        public static event System.Action<GrandVault, VaultMoment, Core.TeamID, Core.TeamID, Network.NetworkPlayer> Moment;

        [Header("The vault")]
        [SerializeField] private Core.VaultMethod methods = Core.VaultMethod.All;
        [SerializeField] private Core.VaultPerk perk = Core.VaultPerk.IronSkin;
        [Tooltip("Centre of the strongroom's floor, in the world.")]
        [SerializeField] private Vector3 roomCentre;
        [Tooltip("The strongroom's inside size, x and z, in metres.")]
        [SerializeField] private Vector2 roomSize = new Vector2(12f, 10f);
        [Tooltip("Where the perk pedestal stands. Zero uses the centre of the room.")]
        [SerializeField] private Vector3 perkPoint;
        [Tooltip("Optional weak wall into the room. Breaking it opens the vault's contents without opening the door.")]
        [SerializeField] private Breakable weakWall;
        [SerializeField] private float interactRadius = 3.4f;

        [Header("Key")]
        [Tooltip("Guards standing within this distance of the door when the match starts are the vault's own: the key falls when the last of them dies.")]
        [SerializeField] private float detailRadius = 16f;
        [SerializeField] private float keyPickupRadius = 1.7f;

        [Header("Drill")]
        [Tooltip("Seconds of drilling to get through the door.")]
        [SerializeField] private float drillSeconds = 50f;
        [Tooltip("Seconds of holding Interact to set the drill.")]
        [SerializeField] private float drillSetSeconds = 2f;
        [Tooltip("Seconds a rival holds Interact to pull the plug.")]
        [SerializeField] private float drillStopSeconds = 3f;
        [Tooltip("One of the drilling crew must be within this distance of the door, or the drill runs backward.")]
        [SerializeField] private float defendRadius = 14f;
        [Tooltip("Seconds of progress lost per second with nobody defending.")]
        [SerializeField] private float drillBackwardRate = 0.6f;
        [SerializeField] private float alarmOnDrill = 25f;

        [Header("Inside")]
        [SerializeField] private int legendaryItems = 3;
        [SerializeField] private int rareItems = 2;
        // [v0.22] [SerializeField] private int cashPiles = 4;      // a fixed four piles of 400 made one vault a whole quota
        // [v0.22] [SerializeField] private int cashPerPile = 400;
        [Tooltip("(v0.23) The most cash piles laid out to make up the rest of the vault's worth.")]
        [SerializeField] private int cashPiles = 4;
        [Tooltip("(v0.23) The most any one cash pile is worth.")]
        [SerializeField] private int cashPerPile = 250;
        [Tooltip("(v0.23) What the whole vault is worth, as a share of the getaway quota. Below 1, so that one vault and a little extra calls the getaway and one vault alone does not.")]
        [Range(0.3f, 1.2f)] [SerializeField] private float haulShareOfQuota = 0.8f;
        [SerializeField] private float lootLifetime = 900f;
        [Tooltip("Seconds of holding Interact at the pedestal to take the perk for the crew.")]
        [SerializeField] private float perkClaimSeconds = 1.5f;
        [SerializeField] private float perkRadius = 2.4f;

        [Networked] public NetworkBool Opened { get; set; }
        [Networked] public Core.TeamID OpenedBy { get; set; }
        [Networked] public Core.VaultOpening OpenedHow { get; set; }

        [Networked] public Core.VaultKeyState KeyState { get; set; }
        [Networked] public Vector3 KeyPosition { get; set; }
        [Networked] public NetworkId KeyHolder { get; set; }
        /// <summary>How many of the vault's own guards are still alive.</summary>
        [Networked] public int GuardsLeft { get; set; }

        [Networked] public Core.TeamID DrillTeam { get; set; }
        /// <summary>Seconds of drilling done, out of <see cref="DrillTotal"/>.</summary>
        [Networked] public float DrillDone { get; set; }
        /// <summary>True while one of the drilling crew is close enough to keep it running.</summary>
        [Networked] public NetworkBool DrillDefended { get; set; }
        /// <summary>0 to 1: a crew setting the drill.</summary>
        [Networked] public float SetProgress { get; set; }
        [Networked] public Core.TeamID SetTeam { get; set; }
        /// <summary>0 to 1: a rival pulling the plug.</summary>
        [Networked] public float StopProgress { get; set; }

        [Networked] public Core.TeamID PerkOwner { get; set; }
        [Networked] public float PerkProgress { get; set; }

        private VaultDoor door;
        private readonly List<AI.BaseAIAgent> detail = new List<AI.BaseAIAgent>();
        private bool detailRegistered;
        private Vector3 lastGuardPosition;

        public string VaultName => Door != null ? Door.VaultName : name;
        public VaultDoor Door => door != null ? door : (door = GetComponent<VaultDoor>());
        public Core.VaultMethod Methods => methods;
        public bool Allows(Core.VaultMethod method) => (methods & method) != 0;
        public Core.VaultPerk Perk => perk;
        public float InteractRadius => interactRadius;
        public float DefendRadius => defendRadius;
        public float DrillTotal => drillSeconds;
        public float DrillRemaining => Mathf.Max(0f, drillSeconds - DrillDone);
        public bool IsDrilling => Object != null && !Opened && DrillTeam != Core.TeamID.None;
        public bool PerkAvailable => Object != null && Opened && perk != Core.VaultPerk.None && PerkOwner == Core.TeamID.None;
        /// <summary>The door, at floor level.</summary>
        public Vector3 DoorPoint => new Vector3(transform.position.x, roomCentre.y, transform.position.z);
        public Vector3 RoomCentre => roomCentre;
        public Vector3 PerkPoint => perkPoint.sqrMagnitude > 0.001f ? perkPoint : roomCentre;
        /// <summary>Unit vector from the room out through the door.</summary>
        public Vector3 Outward
        {
            get
            {
                Vector3 d = DoorPoint - roomCentre; d.y = 0f;
                return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
            }
        }

        public bool InDoorRange(Vector3 position)
        {
            Vector3 d = position - DoorPoint;
            return Mathf.Abs(d.y) < 4f && d.x * d.x + d.z * d.z <= interactRadius * interactRadius;
        }

        public bool InPerkRange(Vector3 position)
        {
            Vector3 d = position - PerkPoint;
            return Mathf.Abs(d.y) < 3f && d.x * d.x + d.z * d.z <= perkRadius * perkRadius;
        }

        public bool InDefendRange(Vector3 position)
        {
            Vector3 d = position - DoorPoint;
            return Mathf.Abs(d.y) < 5f && d.x * d.x + d.z * d.z <= defendRadius * defendRadius;
        }

        /// <summary>The player carrying this vault's key, on any peer.</summary>
        public Network.NetworkPlayer KeyCarrier
        {
            get
            {
                if (Object == null || KeyState != Core.VaultKeyState.Carried || KeyHolder == default) return null;
                return Runner.TryFindObject(KeyHolder, out NetworkObject obj) && obj != null ? obj.GetComponent<Network.NetworkPlayer>() : null;
            }
        }

        public bool IsCarriedBy(Network.NetworkPlayer player)
        {
            return player != null && player.Object != null && Object != null && KeyState == Core.VaultKeyState.Carried && KeyHolder == player.Object.Id;
        }

        /// <summary>The nearest vault that is still shut, or null when every vault is open.</summary>
        public static GrandVault NearestShut(Vector3 from, out float distance)
        {
            GrandVault best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                if (v == null || v.Object == null || v.Opened) continue;
                float sq = (v.DoorPoint - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = v; }
            }
            distance = best != null ? Mathf.Sqrt(bestSq) : 0f;
            return best;
        }

        /// <summary>(v0.22) True if <paramref name="agent"/> is one of a vault's own guards. State authority only: that is where the lists are.</summary>
        public static bool IsVaultGuard(AI.BaseAIAgent agent)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].detail.Contains(agent)) return true;
            return false;
        }

        /// <summary>The vault whose key <paramref name="player"/> carries, or null.</summary>
        public static GrandVault KeyCarriedBy(Network.NetworkPlayer player)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].IsCarriedBy(player)) return All[i];
            return null;
        }

        // ------------------------------------------------------------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Object.HasStateAuthority)
            {
                ResetVault();
                AI.BaseAIAgent.AgentDied += OnGuardDied;
            }
            BuildModels();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            AI.BaseAIAgent.AgentDied -= OnGuardDied;
        }

        private void ResetVault()
        {
            Opened = false;
            OpenedBy = Core.TeamID.None;
            KeyState = Allows(Core.VaultMethod.Key) ? Core.VaultKeyState.Guarded : Core.VaultKeyState.None;
            KeyHolder = default;
            GuardsLeft = 0;
            DrillTeam = Core.TeamID.None;
            DrillDone = 0f;
            SetProgress = 0f;
            SetTeam = Core.TeamID.None;
            StopProgress = 0f;
            PerkOwner = Core.TeamID.None;
            PerkProgress = 0f;
            detail.Clear();
            detailRegistered = false;
        }

        // ------------------------------------------------------------------
        // Simulation (state authority)

        private readonly List<Network.NetworkPlayer> atDoor = new List<Network.NetworkPlayer>();

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;
            float dt = Runner.DeltaTime;

            if (Opened) { TickPerk(dt); return; }

            // Something else opened the door (the map's time-lock event): the vault is open all the same.
            if (Door != null && Door.IsOpen) { Open(Core.TeamID.None, Core.VaultOpening.TimeLock); return; }
            if (weakWall != null && weakWall.Object != null && weakWall.IsBroken) { Open(Core.TeamID.None, Core.VaultOpening.Wall); return; }

            if (!detailRegistered && gm.MatchElapsedSeconds > 2f) RegisterDetail();
            TickKey();

            // Who is working the door this tick.
            atDoor.Clear();
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch || !InDoorRange(p.Position)) continue;
                if (p.HasStatus(Core.StatusFlags.Interacting) && !p.HasStatus(Core.StatusFlags.EMPDisabled)) atDoor.Add(p);
            }

            // The key opens it at once.
            if (KeyState == Core.VaultKeyState.Carried)
            {
                for (int i = 0; i < atDoor.Count; i++)
                {
                    if (atDoor[i].Object.Id != KeyHolder) continue;
                    Open(atDoor[i].Team, Core.VaultOpening.Key);
                    return;
                }
            }

            if (Allows(Core.VaultMethod.Drill)) TickDrill(dt, all);
        }

        private void RegisterDetail()
        {
            detailRegistered = true;
            if (!Allows(Core.VaultMethod.Key)) return;
            detail.Clear();
            var agents = AI.BaseAIAgent.All;
            Vector3 at = DoorPoint;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a == null || a.Object == null || !a.IsAlive) continue;
                Vector3 d = a.transform.position - at;
                if (Mathf.Abs(d.y) < 4f && d.x * d.x + d.z * d.z <= detailRadius * detailRadius) detail.Add(a);
            }
            GuardsLeft = detail.Count;
            // A vault nobody guards leaves its key by the door: the route still works, it is just not a fight.
            if (detail.Count == 0) DropKey(DoorPoint + Outward * 2.5f);
        }

        private void OnGuardDied(AI.BaseAIAgent agent)
        {
            if (Object == null || !Object.HasStateAuthority || Opened || !detail.Contains(agent)) return;
            int registered = detail.Count;
            lastGuardPosition = agent.transform.position;
            detail.Remove(agent);
            for (int i = detail.Count - 1; i >= 0; i--) if (detail[i] == null || detail[i].Object == null || !detail[i].IsAlive) detail.RemoveAt(i);
            GuardsLeft = detail.Count;
            if (KeyState == Core.VaultKeyState.Guarded && Core.VaultRules.DetailCleared(registered, detail.Count)) DropKey(lastGuardPosition);
        }

        private void DropKey(Vector3 near)
        {
            // Onto the floor under wherever its carrier was, a little above it so it can be seen.
            Vector3 from = near + Vector3.up * 0.6f;
            Vector3 at = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point : near;
            KeyPosition = at + Vector3.up * 0.9f;
            KeyHolder = default;
            KeyState = Core.VaultKeyState.Dropped;
            RPC_Moment((byte)VaultMoment.KeyDropped, Core.TeamID.None, Core.TeamID.None, default);
        }

        private void TickKey()
        {
            if (KeyState == Core.VaultKeyState.Dropped)
            {
                var all = Network.NetworkPlayer.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                    Vector3 d = p.Position - KeyPosition;
                    if (Mathf.Abs(d.y) > 2.5f || d.x * d.x + d.z * d.z > keyPickupRadius * keyPickupRadius) continue;
                    KeyHolder = p.Object.Id;
                    KeyState = Core.VaultKeyState.Carried;
                    RPC_Moment((byte)VaultMoment.KeyTaken, p.Team, Core.TeamID.None, p.Object.Id);
                    return;
                }
            }
            else if (KeyState == Core.VaultKeyState.Carried)
            {
                var holder = KeyCarrier;
                if (holder != null && holder.IsActiveInMatch) KeyPosition = holder.Position; // remembered, for when they fall
                else DropKey(KeyPosition);
            }
        }

        private void TickDrill(float dt, IReadOnlyList<Network.NetworkPlayer> all)
        {
            if (DrillTeam == Core.TeamID.None)
            {
                // Setting the drill: the first crew at the door holds Interact for a moment.
                Network.NetworkPlayer setter = null;
                for (int i = 0; i < atDoor.Count; i++)
                {
                    if (SetTeam != Core.TeamID.None && atDoor[i].Team != SetTeam) continue;
                    setter = atDoor[i];
                    break;
                }
                if (setter == null && atDoor.Count > 0 && SetProgress <= 0f) setter = atDoor[0];
                if (setter != null) SetTeam = setter.Team;

                float seconds = drillSetSeconds * Core.VaultRules.ActionTime(GameMode.ExtractionHeistGameMode.PerksOf(SetTeam));
                SetProgress = Core.VaultRules.HoldStep(SetProgress, setter != null, dt, seconds, 2f);
                if (SetProgress <= 0f) SetTeam = Core.TeamID.None;
                if (setter == null || SetProgress < 1f) return;

                DrillTeam = SetTeam;
                DrillDone = 0f;
                DrillDefended = true;
                SetProgress = 0f;
                SetTeam = Core.TeamID.None;
                StopProgress = 0f;
                GameMode.AlarmSystem.Raise(alarmOnDrill, VaultName + " drill");
                AI.AIBlackboard.ReportNoise(DoorPoint, 60f);
                RPC_Moment((byte)VaultMoment.DrillStarted, DrillTeam, Core.TeamID.None, setter.Object.Id);
                return;
            }

            // Running. A rival at the door can pull the plug.
            Network.NetworkPlayer rival = null;
            for (int i = 0; i < atDoor.Count; i++) if (atDoor[i].Team != DrillTeam) { rival = atDoor[i]; break; }
            float stopSeconds = drillStopSeconds * (rival != null ? Core.VaultRules.ActionTime(GameMode.ExtractionHeistGameMode.PerksOf(rival.Team)) : 1f);
            StopProgress = Core.VaultRules.HoldStep(StopProgress, rival != null, dt, stopSeconds, 1.5f);
            if (rival != null && StopProgress >= 1f)
            {
                var owner = DrillTeam;
                StopDrill();
                RPC_Moment((byte)VaultMoment.DrillStopped, rival.Team, owner, rival.Object.Id);
                return;
            }

            bool defended = false;
            for (int i = 0; i < all.Count && !defended; i++)
            {
                var p = all[i];
                if (p != null && p.Object != null && p.IsActiveInMatch && p.Team == DrillTeam && InDefendRange(p.Position)) defended = true;
            }
            DrillDefended = defended;
            DrillDone = Core.VaultRules.DrillStep(DrillDone, defended, dt, drillSeconds, drillBackwardRate);

            if (Core.VaultRules.DrillAbandoned(DrillDone, defended))
            {
                var owner = DrillTeam;
                StopDrill();
                RPC_Moment((byte)VaultMoment.DrillStopped, Core.TeamID.None, owner, default);
                return;
            }
            // A drill is not a quiet thing: guards keep being told where it is.
            int every = Mathf.Max(1, Mathf.RoundToInt(6f / dt));
            if (((int)Runner.Tick) % every == 0) AI.AIBlackboard.ReportNoise(DoorPoint, 45f);

            if (DrillDone >= drillSeconds) Open(DrillTeam, Core.VaultOpening.Drill);
        }

        private void StopDrill()
        {
            DrillTeam = Core.TeamID.None;
            DrillDone = 0f;
            DrillDefended = false;
            StopProgress = 0f;
            SetProgress = 0f;
            SetTeam = Core.TeamID.None;
        }

        /// <summary>The lock console beside the door was cracked. State authority only.</summary>
        public void OpenByCrack(Core.TeamID team)
        {
            if (Object.HasStateAuthority && !Opened) Open(team, Core.VaultOpening.Crack);
        }

        private void Open(Core.TeamID team, Core.VaultOpening how)
        {
            Opened = true;
            OpenedBy = team;
            OpenedHow = how;
            if (KeyState != Core.VaultKeyState.None) KeyState = Core.VaultKeyState.Used;
            KeyHolder = default;
            StopDrill();
            if (how != Core.VaultOpening.Wall && Door != null) Door.ForceOpen(team, how.ToString());
            SpawnLoot();
            GameMode.AlarmSystem.Raise(20f, VaultName + " open");
            RPC_Moment((byte)VaultMoment.Opened, team, Core.TeamID.None, default);
        }

        // ---- the loot, laid out in the room

        private static readonly List<Vector3> spots = new List<Vector3>();
        private static readonly List<int> tierIds = new List<int>();
        private static readonly int[] picks = new int[8];

        private void SpawnLoot()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var catalog = Data.LootCatalog.Active;
            if (gm == null || !gm.CanSpawnLoot || catalog == null) return;

            // Every clear patch of floor on a 2 m grid, nearest the middle first, leaving the pedestal alone.
            spots.Clear();
            float hx = Mathf.Max(1f, roomSize.x * 0.5f - 1.5f), hz = Mathf.Max(1f, roomSize.y * 0.5f - 1.5f);
            Vector3 pedestal = PerkPoint;
            for (float x = -hx; x <= hx + 0.01f; x += 2f)
            {
                for (float z = -hz; z <= hz + 0.01f; z += 2f)
                {
                    Vector3 at = roomCentre + new Vector3(x, 0f, z);
                    if ((at - pedestal).sqrMagnitude < 2.2f * 2.2f) continue;
                    if (Physics.CheckSphere(at + Vector3.up * 0.7f, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    spots.Add(at);
                }
            }
            Vector3 centre = roomCentre;
            spots.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));

            int placed = 0;
            Vector3 Next()
            {
                // More loot than clear floor: ring the rest round the pedestal rather than lose it.
                if (placed < spots.Count) return spots[placed++] + Vector3.up * 0.5f;
                float angle = placed++ * 2.399963f;
                return pedestal + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f + Vector3.up * 0.5f;
            }

            int seed = unchecked((int)Runner.Tick * 397 ^ VaultName.GetHashCode());
            // (v0.23) The vault is worth a share of the quota. Items are laid until the next would go over it,
            //         and cash makes up the rest, so the total holds whatever the map's quota is.
            int target = Core.VaultRules.HaulTarget(gm.LootQuota, haulShareOfQuota);
            int laid = 0;
            void Lay(Data.LootTier tier, int count, int tierSeed)
            {
                catalog.GetIdsOfTier(tier, tierIds);
                int n = Core.MatchRules.PickDistinct(tierSeed, tierIds.Count, Mathf.Min(count, picks.Length), picks);
                for (int i = 0; i < n; i++)
                {
                    int id = tierIds[picks[i]];
                    int value = catalog.ValueOf(id);
                    // [v0.22] gm.SpawnLoot(Next(), catalog.ValueOf(id), id, lootLifetime);
                    if (target > 0 && !Core.VaultRules.FitsHaul(laid, value, target)) continue;
                    if (gm.SpawnLoot(Next(), value, id, lootLifetime)) laid += value;
                }
            }
            Lay(Data.LootTier.Legendary, legendaryItems, seed);
            Lay(Data.LootTier.Rare, rareItems, seed + 1);
            // [v0.22] for (int i = 0; i < cashPiles; i++) gm.SpawnLoot(Next(), cashPerPile, 0, lootLifetime);
            int piles = Core.VaultRules.CashPiles(target > 0 ? target - laid : cashPiles * cashPerPile, cashPerPile, cashPiles, out int perPile);
            for (int i = 0; i < piles; i++) gm.SpawnLoot(Next(), perPile, 0, lootLifetime);
        }

        // ---- the perk

        private void TickPerk(float dt)
        {
            if (perk == Core.VaultPerk.None || PerkOwner != Core.TeamID.None) return;
            Network.NetworkPlayer claimer = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch || !InPerkRange(p.Position)) continue;
                if (!p.HasStatus(Core.StatusFlags.Interacting) || p.HasStatus(Core.StatusFlags.EMPDisabled)) continue;
                claimer = p;
                break;
            }
            float seconds = perkClaimSeconds * (claimer != null ? Core.VaultRules.ActionTime(GameMode.ExtractionHeistGameMode.PerksOf(claimer.Team)) : 1f);
            PerkProgress = Core.VaultRules.HoldStep(PerkProgress, claimer != null, dt, seconds, 1.5f);
            if (claimer == null || PerkProgress < 1f) return;

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            PerkProgress = 0f;
            if (!gm.GrantTeamPerk(claimer.Team, perk)) return; // that crew already holds this one: leave it for another
            PerkOwner = claimer.Team;
            RPC_Moment((byte)VaultMoment.PerkClaimed, claimer.Team, Core.TeamID.None, claimer.Object.Id);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Moment(byte moment, Core.TeamID team, Core.TeamID other, NetworkId who)
        {
            Network.NetworkPlayer player = null;
            if (who != default && Runner.TryFindObject(who, out NetworkObject obj) && obj != null) player = obj.GetComponent<Network.NetworkPlayer>();
            var what = (VaultMoment)moment;
            Vector3 at = DoorPoint + Vector3.up * 1.5f;
            switch (what)
            {
                case VaultMoment.DrillStarted: Art.Fx.Flash(at, new Color(1f, 0.7f, 0.2f), 12f, 4f, 0.5f); break;
                case VaultMoment.DrillStopped: Art.Fx.Shock(at, 3f, new Color(1f, 0.3f, 0.2f), 0.4f); break;
                case VaultMoment.Opened:
                    Art.Fx.Ring(DoorPoint + Vector3.up * 0.1f, 9f, Art.ThreatOutline.Objective, 1.1f, 0.22f);
                    Art.Fx.Flash(at, Art.ThreatOutline.Objective, 20f, 5f, 0.8f);
                    break;
                case VaultMoment.PerkClaimed:
                    Art.Fx.Burst(PerkPoint + Vector3.up * 1.4f, PerkColour(perk), 30, 5f, 0.1f, 0.9f, -0.3f);
                    break;
            }
            Moment?.Invoke(this, what, team, other, player);
        }

        // ------------------------------------------------------------------
        // What it looks like: a vault door, a drill, a key, a pedestal. Built in code on every peer.

        private Transform key, drill, gem;
        private Renderer pedestalLamp, frameLampA, frameLampB;
        private int shownLamp = -1;
        private bool shownPerk = true;
        private float nextSpark, nextBuzz;

        public static Color PerkColour(Core.VaultPerk perk)
        {
            switch (perk)
            {
                case Core.VaultPerk.IronSkin:   return new Color(0.5f, 0.75f, 1f);
                case Core.VaultPerk.HotShots:   return new Color(1f, 0.45f, 0.15f);
                case Core.VaultPerk.SecondWind: return new Color(0.4f, 1f, 0.6f);
                case Core.VaultPerk.QuickHands: return new Color(1f, 0.9f, 0.3f);
                default:                        return Color.white;
            }
        }

        private void BuildModels()
        {
            var steel = Art.ArtMaterials.Colour(new Color(0.36f, 0.38f, 0.43f));
            var dark = Art.ArtMaterials.Colour(new Color(0.12f, 0.12f, 0.15f));
            var brass = Art.ArtMaterials.Colour(new Color(0.86f, 0.66f, 0.22f));

            // ---- the door: a slab with a round hatch and a wheel on each face. It rides on the door's own
            //      slab, so it sinks into the floor with it when the vault opens.
            var slab = Door != null ? Door.DoorVisual : null;
            float width = 4f, height = 4f;
            if (slab != null)
            {
                var slabRenderer = slab.GetComponent<Renderer>();
                if (slabRenderer != null) slabRenderer.enabled = false; // the scene's plain box
                Vector3 s = slab.transform.lossyScale;
                width = Mathf.Max(1f, s.x); height = Mathf.Max(1f, s.y);
                var face = Art.Prim.Group(slab.transform, "Vault Door");
                face.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
                Art.Prim.Box(face, "Slab", Vector3.zero, new Vector3(width, height, 0.5f), steel);
                float round = Mathf.Min(width, height) * 0.82f;
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = side * 0.27f;
                    Art.Prim.Rod(face, "Hatch", new Vector3(0f, 0f, z), new Vector3(round, 0.03f, round), dark, new Vector3(90f, 0f, 0f));
                    Art.Prim.Rod(face, "Hatch Rim", new Vector3(0f, 0f, z - side * 0.01f), new Vector3(round + 0.24f, 0.02f, round + 0.24f), brass, new Vector3(90f, 0f, 0f));
                    Art.Prim.Rod(face, "Hub", new Vector3(0f, 0f, z + side * 0.08f), new Vector3(0.5f, 0.06f, 0.5f), brass, new Vector3(90f, 0f, 0f));
                    for (int spoke = 0; spoke < 3; spoke++)
                        Art.Prim.Box(face, "Spoke", new Vector3(0f, 0f, z + side * 0.07f), new Vector3(round * 0.62f, 0.09f, 0.06f), brass, new Vector3(0f, 0f, spoke * 60f));
                    for (int bolt = 0; bolt < 10; bolt++)
                    {
                        float a = bolt * Mathf.PI * 2f / 10f;
                        Art.Prim.Rod(face, "Bolt", new Vector3(Mathf.Cos(a) * round * 0.43f, Mathf.Sin(a) * round * 0.43f, z + side * 0.03f), new Vector3(0.16f, 0.04f, 0.16f), steel, new Vector3(90f, 0f, 0f));
                    }
                }
            }

            // ---- the frame over the door, which stays put: two lamps that say what state the vault is in
            var frame = Art.Prim.Group(transform, "Vault Frame");
            Vector3 own = transform.lossyScale;
            frame.localScale = new Vector3(1f / Mathf.Max(0.01f, own.x), 1f / Mathf.Max(0.01f, own.y), 1f / Mathf.Max(0.01f, own.z));
            float top = height * 0.5f;
            Art.Prim.Box(frame, "Lintel", new Vector3(0f, top + 0.22f, 0f), new Vector3(width + 0.6f, 0.44f, 2.3f), dark);
            Art.Prim.Box(frame, "Lintel Trim", new Vector3(0f, top - 0.02f, 0f), new Vector3(width + 0.66f, 0.06f, 2.36f), brass);
            frameLampA = Art.Prim.Box(frame, "Lamp", new Vector3(0f, top + 0.22f, 1.17f), new Vector3(width * 0.5f, 0.2f, 0.05f), dark).GetComponent<Renderer>();
            frameLampB = Art.Prim.Box(frame, "Lamp", new Vector3(0f, top + 0.22f, -1.17f), new Vector3(width * 0.5f, 0.2f, 0.05f), dark).GetComponent<Renderer>();

            // ---- the drill: a rig that stands at the outside face while a crew is drilling
            drill = Art.Prim.Group(null, VaultName + " Drill");
            drill.SetParent(transform, true);
            drill.position = DoorPoint + Outward * 1.6f;
            drill.rotation = Quaternion.LookRotation(-Outward, Vector3.up);
            drill.localScale = frame.localScale;
            var hazard = Art.ArtMaterials.Colour(new Color(0.95f, 0.75f, 0.1f));
            for (int leg = 0; leg < 3; leg++)
            {
                var turn = Quaternion.Euler(0f, leg * 120f, 0f);
                Art.Prim.Box(drill, "Leg", turn * new Vector3(0f, 0.55f, 0.32f), new Vector3(0.07f, 1.2f, 0.07f), dark, new Vector3(-18f, leg * 120f, 0f));
            }
            Art.Prim.Box(drill, "Motor", new Vector3(0f, 1.25f, 0f), new Vector3(0.42f, 0.42f, 0.7f), hazard);
            Art.Prim.Rod(drill, "Bit", new Vector3(0f, 1.25f, 0.75f), new Vector3(0.1f, 0.45f, 0.1f), steel, new Vector3(90f, 0f, 0f));
            Art.Prim.Ball(drill, "Light", new Vector3(0f, 1.56f, -0.2f), new Vector3(0.16f, 0.16f, 0.16f), Art.ArtMaterials.Emissive(new Color(1f, 0.6f, 0.1f)));
            drill.gameObject.SetActive(false);

            // ---- the key: a card on a ring, turning in the air where it fell
            key = Art.Prim.Group(null, VaultName + " Key");
            key.SetParent(transform, true);
            key.localScale = frame.localScale;
            var gold = Art.ArtMaterials.Emissive(Art.ThreatOutline.Objective);
            Art.Prim.Box(key, "Card", Vector3.zero, new Vector3(0.5f, 0.32f, 0.04f), gold);
            Art.Prim.Box(key, "Stripe", new Vector3(0f, 0.06f, 0f), new Vector3(0.5f, 0.06f, 0.05f), dark);
            Art.Prim.Rod(key, "Ring", new Vector3(-0.3f, 0.12f, 0f), new Vector3(0.16f, 0.015f, 0.16f), brass, new Vector3(90f, 0f, 0f));
            Art.ThreatOutline.Attach(key.gameObject, Art.ThreatOutline.Objective, always: true);
            key.gameObject.SetActive(false);

            // ---- the pedestal and the perk on it
            if (perk != Core.VaultPerk.None)
            {
                var stand = Art.Prim.Group(null, VaultName + " Perk");
                stand.SetParent(transform, true);
                stand.position = PerkPoint;
                stand.rotation = Quaternion.identity;
                stand.localScale = frame.localScale;
                Art.Prim.Rod(stand, "Base", new Vector3(0f, 0.08f, 0f), new Vector3(1.3f, 0.08f, 1.3f), dark);
                Art.Prim.Rod(stand, "Column", new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.42f, 0.6f), steel);
                Art.Prim.Rod(stand, "Top", new Vector3(0f, 1f, 0f), new Vector3(0.9f, 0.04f, 0.9f), brass);
                pedestalLamp = Art.Prim.Rod(stand, "Glow", new Vector3(0f, 1.05f, 0f), new Vector3(0.6f, 0.012f, 0.6f), Art.ArtMaterials.Emissive(PerkColour(perk))).GetComponent<Renderer>();
                gem = Art.Prim.Group(stand, "Perk Gem", new Vector3(0f, 1.6f, 0f));
                var glow = Art.ArtMaterials.Emissive(PerkColour(perk));
                Art.Prim.Box(gem, "Gem", Vector3.zero, new Vector3(0.36f, 0.36f, 0.36f), glow, new Vector3(45f, 0f, 45f));
                Art.Prim.Box(gem, "Gem Core", Vector3.zero, new Vector3(0.2f, 0.56f, 0.2f), glow, new Vector3(0f, 45f, 0f));
                Art.ThreatOutline.Attach(gem.gameObject, PerkColour(perk), always: true);
                gem.gameObject.SetActive(false);
            }
        }

        public override void Render()
        {
            if (key == null) return;

            // 0 shut, 1 being drilled, 2 open
            int lamp = Opened ? 2 : (IsDrilling ? 1 : 0);
            if (lamp != shownLamp)
            {
                shownLamp = lamp;
                var glow = Art.ArtMaterials.Emissive(lamp == 2 ? new Color(0.35f, 1f, 0.55f) : lamp == 1 ? new Color(1f, 0.6f, 0.1f) : new Color(0.85f, 0.14f, 0.1f));
                if (frameLampA != null) frameLampA.sharedMaterial = glow;
                if (frameLampB != null) frameLampB.sharedMaterial = glow;
            }

            bool drilling = IsDrilling;
            if (drill.gameObject.activeSelf != drilling) drill.gameObject.SetActive(drilling);
            if (drilling && DrillDefended)
            {
                Vector3 bite = DoorPoint + Outward * 0.5f + Vector3.up * 1.25f;
                if (Time.time >= nextSpark)
                {
                    nextSpark = Time.time + 0.18f;
                    Art.Fx.Burst(bite, new Color(1f, 0.8f, 0.3f, 1f), 5, 3.5f, 0.05f, 0.3f, 1.2f);
                }
                if (Time.time >= nextBuzz)
                {
                    nextBuzz = Time.time + 0.5f;
                    Audio.Sfx.PlayAt(Core.ProceduralSfx.DrillBuzz, bite, 0.9f, Audio.Sfx.Room); // [v0.22] AudioSource.PlayClipAtPoint
                }
            }

            bool keyOut = !Opened && KeyState == Core.VaultKeyState.Dropped;
            if (key.gameObject.activeSelf != keyOut) key.gameObject.SetActive(keyOut);
            if (keyOut)
            {
                key.position = KeyPosition + Vector3.up * (Mathf.Sin(Time.time * 2.4f) * 0.12f);
                key.rotation = Quaternion.Euler(0f, Time.time * 110f, 0f);
            }

            if (gem != null)
            {
                bool perkOut = PerkAvailable;
                if (perkOut != shownPerk || gem.gameObject.activeSelf != perkOut)
                {
                    shownPerk = perkOut;
                    gem.gameObject.SetActive(perkOut);
                    if (pedestalLamp != null)
                    {
                        Color c = PerkOwner != Core.TeamID.None ? Art.CrewStyle.Get(PerkOwner).Primary : PerkColour(perk);
                        pedestalLamp.sharedMaterial = Opened ? Art.ArtMaterials.Emissive(c) : Art.ArtMaterials.Colour(new Color(0.12f, 0.12f, 0.15f));
                    }
                }
                if (perkOut)
                {
                    gem.localPosition = new Vector3(0f, 1.6f + Mathf.Sin(Time.time * 2f) * 0.1f, 0f);
                    gem.localRotation = Quaternion.Euler(0f, Time.time * 70f, 0f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireCube(roomCentre + Vector3.up * 1.5f, new Vector3(roomSize.x, 3f, roomSize.y));
            Gizmos.DrawWireSphere(PerkPoint, perkRadius);
            Gizmos.color = new Color(1f, 0.4f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, defendRadius);
        }
    }
}
