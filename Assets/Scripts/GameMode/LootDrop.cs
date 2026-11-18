using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Loot dropped by a player who died while carrying it. Any living player who walks over it picks it
    /// up (including the original owner's teammates), which makes contested fights over carriers
    /// meaningful. Despawns on pickup or after <see cref="lifetime"/> seconds.
    /// </summary>
    public class LootDrop : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 1.5f;
        [SerializeField] private float lifetime = 120f;
        [Tooltip("Seconds before anyone can pick it up, so the killer doesn't instantly hoover it.")]
        [SerializeField] private float pickupDelay = 1f;

        [Networked] public int Value { get; set; }
        /// <summary>(v0.10) Loot catalog id of a named item, or 0 for loose loot that is just a number.</summary>
        [Networked] public int ItemId { get; set; }

        /// <summary>(v0.10) Live registry on this peer (bots and HUD look pickups up here).</summary>
        public static readonly System.Collections.Generic.List<LootDrop> All = new System.Collections.Generic.List<LootDrop>();
        /// <summary>(v0.10) Fired on every peer when someone takes loot (who, catalog id or 0, value).</summary>
        public static event System.Action<Network.NetworkPlayer, int, int> PickedUp;

        private float lifetimeOverride;

        public float PickupRadius => pickupRadius;
        public bool IsPickupReady => PickupDelayTimer.ExpiredOrNotRunning(Runner);
        public Data.LootItemDef Item => ItemId > 0 ? Data.LootCatalog.Active.Get(ItemId) : null;

        /// <summary>
        /// (v0.10) Sets what this pickup is. Call from the spawn callback, before <see cref="Spawned"/> runs.
        /// <paramref name="lifetimeSeconds"/> of 0 keeps the prefab's default.
        /// </summary>
        public void Configure(int value, int itemId, float lifetimeSeconds = 0f)
        {
            Value = value;
            ItemId = itemId;
            lifetimeOverride = lifetimeSeconds;
        }
        [Networked] private TickTimer LifetimeTimer { get; set; }
        [Networked] private TickTimer PickupDelayTimer { get; set; }

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Object.HasStateAuthority)
            {
                // [v0.9] LifetimeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
                LifetimeTimer = TickTimer.CreateFromSeconds(Runner, lifetimeOverride > 0f ? lifetimeOverride : lifetime);
                PickupDelayTimer = TickTimer.CreateFromSeconds(Runner, pickupDelay);
            }
            ApplyLook();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        /// <summary>(v0.10) Named items are tinted by tier and sized by value so a glance tells what is worth grabbing.</summary>
        private void ApplyLook()
        {
            var item = Item;
            if (item == null) return;
            // [v0.11] tinted the prefab's sphere by tier and scaled it by value
            // (v0.12) named items get a model of the thing itself, on a ring in the tier colour
            var sphere = GetComponentInChildren<Renderer>();
            if (sphere != null) sphere.enabled = false;

            // The prefab is scaled down (it was a small sphere); build the model in real metres underneath it.
            var holder = new GameObject("Model").transform;
            holder.SetParent(transform, false);
            Vector3 s = transform.lossyScale;
            holder.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            float size = item.tier == Data.LootTier.Legendary ? 1.35f : (item.tier == Data.LootTier.Rare ? 1.15f : 1f);
            var model = Art.ArtefactBuilder.Build(holder, item.shape, item.tier);
            model.localScale = Vector3.one * size;
            model.gameObject.AddComponent<Art.PickupSpin>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (LifetimeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
                return;
            }

            if (!PickupDelayTimer.ExpiredOrNotRunning(Runner)) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var candidates = Network.NetworkPlayer.FindPlayersInRadius(transform.position, pickupRadius, Core.TeamID.None, aliveOnly: true);
            foreach (var player in candidates)
            {
                // (v0.10) a named item is taken whole, and only by someone with a free hand
                if (ItemId > 0)
                {
                    if (!player.AddLootItem(ItemId, Value)) continue;
                    RPC_PickedUp(player.PlayerRef, Value, ItemId);
                    Runner.Despawn(Object);
                    return;
                }

                int accepted = player.AddLoot(Value);
                if (accepted <= 0) continue;

                Value -= accepted;
                RPC_PickedUp(player.PlayerRef, accepted, 0);
                if (Value <= 0)
                {
                    Runner.Despawn(Object);
                    return;
                }
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        // [v0.9] private void RPC_PickedUp(PlayerRef player, int amount)
        // (v0.10) the item id travels with the call: the pickup is despawned in the same tick, so its own
        //         networked state may already be gone when this runs on a client.
        private void RPC_PickedUp(PlayerRef player, int amount, int itemId)
        {
            Debug.Log($"[LootDrop] {player} picked up {amount} loot");
            PickedUp?.Invoke(Network.NetworkPlayer.Resolve(player), itemId, amount);
        }
    }
}
