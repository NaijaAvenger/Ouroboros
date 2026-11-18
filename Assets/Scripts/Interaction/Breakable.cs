using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>What a breakable object is. Decides its model, its size and how it comes apart.</summary>
    public enum BreakableKind
    {
        Crate = 0,
        DisplayCase = 1,
        VendingMachine = 2,
        Atm = 3,
        CashRegister = 4,
        Locker = 5,
        /// <summary>Armoured: shrugs off small-arms fire. Needs explosives, a heavy hit or a breaching kit.</summary>
        StrongBox = 6,
        /// <summary>Armoured, and it blocks a passage. Breaking it opens a route. No loot.</summary>
        WeakWall = 7,
        /// <summary>A crate delivered by a map event. Arrives dormant and is armed by the event.</summary>
        AirdropCrate = 8,
        /// <summary>Armoured: an office safe.</summary>
        Safe = 9
    }

    /// <summary>
    /// Something that can be destroyed, with loot inside (v0.14). Shoot a display case, a vending machine or a
    /// crate and it breaks open and spills what it held. Armoured kinds (strongbox, safe, weak wall) ignore hits
    /// below <see cref="armourThreshold"/>, so they need explosives, a heavy round or a breaching kit: that is
    /// the gear puzzle. A weak wall also blocks the way until it is broken.
    ///
    /// Breaking things is loud: it raises the alarm and guards come to look. State authority decides; every
    /// peer sees the object break because <see cref="IsBroken"/> is networked. The model is built in code at
    /// run time (<c>Art.BreakableModels</c>), so the scene only stores the settings.
    /// </summary>
    public class Breakable : NetworkBehaviour, Combat.IDamageable, IBreachable
    {
        public static readonly List<Breakable> All = new List<Breakable>();

        [SerializeField] private BreakableKind kind = BreakableKind.Crate;
        [SerializeField] private string label = "";
        [SerializeField] private float maxHealth = 60f;
        [Tooltip("Hits that do less than this in one go bounce off. 0 = anything hurts it.")]
        [SerializeField] private float armourThreshold = 0f;
        [Header("Loot")]
        [SerializeField] private Data.LootTier lootTier = Data.LootTier.Common;
        [Tooltip("Named items from the catalog spilled when it breaks.")]
        [SerializeField] private int lootItems = 0;
        [Tooltip("Loose cash spilled when it breaks, on top of any items.")]
        [SerializeField] private int looseCash = 0;
        [Header("Consequences")]
        [SerializeField] private float alarmOnBreak = 4f;
        [SerializeField] private float noiseRadius = 22f;
        [Header("Weak wall")]
        [Tooltip("Size of the wall panel in metres (weak walls only).")]
        [SerializeField] private Vector3 wallSize = new Vector3(4f, 4f, 0.5f);
        [Header("Events")]
        [Tooltip("Hidden and untouchable until a map event arms it.")]
        [SerializeField] private bool startsDormant = false;

        [Networked] public float Health { get; set; }
        [Networked] public NetworkBool IsBroken { get; set; }
        [Networked] public NetworkBool IsDormant { get; set; }

        /// <summary>Raised on every peer when an object breaks. Cosmetic.</summary>
        public static event System.Action<Breakable> Broken;
        /// <summary>Raised on every peer when a hit bounces off armour (position). Cosmetic.</summary>
        public static event System.Action<Vector3> Deflected;

        public BreakableKind Kind => kind;
        public string Label => string.IsNullOrEmpty(label) ? DefaultLabel(kind) : label;
        public bool IsArmoured => armourThreshold > 0f;
        public bool HoldsLoot => lootItems > 0 || looseCash > 0;
        public Vector3 WallSize => wallSize;
        public Data.LootTier LootTier => lootTier;

        // ---- IDamageable ----
        public bool IsAlive => Object != null && !IsBroken && !IsDormant;
        public Core.TeamID Team => Core.TeamID.None;
        public Vector3 Position => transform.position;

        // ---- IBreachable ----
        bool IBreachable.IsBreached => IsBroken;

        private Transform modelRoot;
        private Collider[] colliders;
        private UnityEngine.AI.NavMeshObstacle obstacle;
        private bool shownBroken, shownDormant, shownOnce;
        private readonly List<int> tierIds = new List<int>();
        private readonly int[] picks = new int[4];

        public static string DefaultLabel(BreakableKind kind)
        {
            switch (kind)
            {
                case BreakableKind.DisplayCase:    return "Display Case";
                case BreakableKind.VendingMachine: return "Vending Machine";
                case BreakableKind.Atm:            return "Cash Machine";
                case BreakableKind.CashRegister:   return "Till";
                case BreakableKind.Locker:         return "Locker";
                case BreakableKind.StrongBox:      return "Strongbox";
                case BreakableKind.WeakWall:       return "Weak Wall";
                case BreakableKind.AirdropCrate:   return "Delivery Crate";
                case BreakableKind.Safe:           return "Safe";
                default:                           return "Crate";
            }
        }

        public override void Spawned()
        {
            All.Add(this);
            colliders = GetComponentsInChildren<Collider>();
            obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (Object.HasStateAuthority)
            {
                Health = Mathf.Max(1f, maxHealth);
                IsBroken = false;
                IsDormant = startsDormant;
            }
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // the scene's placeholder box
            modelRoot = Art.BreakableModels.Build(transform, kind, wallSize, lootTier);
            shownOnce = false;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void Render()
        {
            if (shownOnce && shownBroken == IsBroken && shownDormant == IsDormant) return;
            bool justBroke = shownOnce && IsBroken && !shownBroken;
            shownOnce = true;
            shownBroken = IsBroken;
            shownDormant = IsDormant;

            bool present = !IsBroken && !IsDormant;
            if (modelRoot != null) modelRoot.gameObject.SetActive(present);
            if (colliders != null) foreach (var c in colliders) if (c != null) c.enabled = present;
            if (obstacle != null) obstacle.enabled = present;

            if (justBroke)
            {
                Art.BreakableModels.BreakEffect(transform.position, kind, wallSize);
                Broken?.Invoke(this);
            }
        }

        // ------------------------------------------------------------------
        // Damage

        public void ApplyDamage(float amount, Network.NetworkPlayer attacker)
        {
            if (!Object.HasStateAuthority || !IsAlive || amount <= 0f) return;

            var info = Combat.DamageContext.Current;
            Vector3 point = info.HasPoint ? info.Point : transform.position + Vector3.up * 0.8f;
            if (armourThreshold > 0f && amount < armourThreshold)
            {
                RPC_Deflected(point);
                return;
            }

            float before = Health;
            Health = Mathf.Max(0f, Health - amount);
            if (attacker != null) attacker.NotifyDamageDealt(before - Health, point, false, Health <= 0f);
            if (Health <= 0f) Break();
        }

        public void ApplyDamage(float amount, Network.NetworkPlayer attacker, Vector3 origin) => ApplyDamage(amount, attacker);

        /// <summary>A breaching kit or charge: goes straight through armour.</summary>
        public void Breach(Core.TeamID byTeam)
        {
            if (!Object.HasStateAuthority || !IsAlive) return;
            Health = 0f;
            Break();
        }

        /// <summary>A map event delivers it: it appears and can be broken.</summary>
        public void Arm()
        {
            if (!Object.HasStateAuthority || !IsDormant) return;
            IsDormant = false;
        }

        private void Break()
        {
            IsBroken = true;
            SpillLoot();
            if (alarmOnBreak > 0f) GameMode.AlarmSystem.Raise(alarmOnBreak, Label);
            if (noiseRadius > 0f) AI.AIBlackboard.ReportNoise(transform.position, noiseRadius);
        }

        private void SpillLoot()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || !gm.CanSpawnLoot) return;

            int spawned = 0;
            Vector3 Spot()
            {
                float angle = spawned * 2.399963f + transform.position.x; // golden angle, turned per object
                spawned++;
                return transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.9f + Vector3.up * 0.35f;
            }

            var catalog = Data.LootCatalog.Active;
            if (lootItems > 0 && catalog != null && catalog.Count > 0)
            {
                catalog.GetIdsOfTier(lootTier, tierIds);
                int seed = unchecked((int)Runner.Tick * 397 ^ (int)Object.Id.Raw * 7919);
                int n = Core.MatchRules.PickDistinct(seed, tierIds.Count, Mathf.Min(lootItems, picks.Length), picks);
                for (int i = 0; i < n; i++)
                {
                    int id = tierIds[picks[i]];
                    gm.SpawnLoot(Spot(), catalog.ValueOf(id), id);
                }
            }
            if (looseCash > 0) gm.SpawnLoot(Spot(), looseCash, 0);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Unreliable)]
        private void RPC_Deflected(Vector3 point)
        {
            Art.BreakableModels.DeflectEffect(point);
            Deflected?.Invoke(point);
        }
    }
}
