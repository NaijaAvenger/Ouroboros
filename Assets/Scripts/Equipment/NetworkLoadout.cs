using System;
using UnityEngine;
using Fusion;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Replicated equipment state for one player: which registry item sits in each slot, magazine and
    /// reserve ammo, per-slot use cooldowns and reload timers. Lives on the player prefab next to
    /// <c>NetworkPlayer</c>. <see cref="EquipmentLoadout"/> watches <see cref="SlotChanged"/> and builds
    /// the local <see cref="BaseEquipment"/> components; weapons call back into this class to spend
    /// ammo and start cooldowns (state authority only).
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class NetworkLoadout : NetworkBehaviour
    {
        public const int SLOTS = Core.GameConstants.MAX_EQUIPMENT_SLOTS;

        /// <summary>Registry id per slot (index + 1); 0 = empty.</summary>
        [Networked, Capacity(SLOTS)] public NetworkArray<int> SlotItemIds => default;
        [Networked, Capacity(SLOTS)] public NetworkArray<int> Magazine => default;
        [Networked, Capacity(SLOTS)] public NetworkArray<int> Reserve => default;
        [Networked, Capacity(SLOTS)] public NetworkArray<TickTimer> UseTimers => default;
        [Networked, Capacity(SLOTS)] public NetworkArray<TickTimer> ReloadTimers => default;

        private Network.NetworkPlayer owner;
        private ChangeDetector changeDetector;
        private readonly int[] knownIds = new int[SLOTS];
        private bool defaultsApplied;

        /// <summary>Fired on every peer when a slot's item id changes (slot index, new data or null).</summary>
        public event Action<int, Data.EquipmentData> SlotChanged;
        /// <summary>Fired on every peer when a weapon fires (loadout, slot, world hit/end point, hit something). Cosmetic.</summary>
        public static event Action<NetworkLoadout, int, Vector3, bool> WeaponFired;
        /// <summary>Fired on every peer when a reload starts (loadout, slot).</summary>
        public static event Action<NetworkLoadout, int> ReloadStarted;

        public Network.NetworkPlayer Owner => owner;

        // ------------------------------------------------------------------

        private void Awake()
        {
            owner = GetComponent<Network.NetworkPlayer>();
            owner.ClassChanged += OnClassChanged;
        }

        private void OnDestroy()
        {
            if (owner != null) owner.ClassChanged -= OnClassChanged;
        }

        public override void Spawned()
        {
            changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            // Build local components for whatever is already equipped (late joiners).
            for (int i = 0; i < SLOTS; i++)
            {
                knownIds[i] = SlotItemIds[i];
                if (knownIds[i] != 0) SlotChanged?.Invoke(i, Registry?.Get(knownIds[i]));
            }

            if (Object.HasStateAuthority && !defaultsApplied && owner.CurrentClass != null)
            {
                EquipDefaults(owner.CurrentClass.ClassData);
            }
        }

        public override void Render()
        {
            if (changeDetector == null) return;
            foreach (var change in changeDetector.DetectChanges(this))
            {
                if (change != nameof(SlotItemIds)) continue;
                for (int i = 0; i < SLOTS; i++)
                {
                    int id = SlotItemIds[i];
                    if (id == knownIds[i]) continue;
                    knownIds[i] = id;
                    SlotChanged?.Invoke(i, Registry?.Get(id));
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            for (int i = 0; i < SLOTS; i++)
            {
                if (ReloadTimers[i].IsRunning && ReloadTimers[i].Expired(Runner))
                {
                    ReloadTimers.Set(i, TickTimer.None);
                    FinishReload(i);
                }
            }
        }

        private static Data.EquipmentRegistry Registry => Data.EquipmentRegistry.Active;

        private void OnClassChanged(Network.NetworkPlayer player, Core.BasePlayerClass playerClass)
        {
            if (Object == null || !Object.HasStateAuthority) return;

            // First class assignment, or a lobby class change: (re)equip that class's defaults.
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            bool preMatch = gm == null || gm.Object == null || !gm.IsMatchLive;
            if (!defaultsApplied || preMatch)
            {
                EquipDefaults(playerClass != null ? playerClass.ClassData : null);
            }
        }

        // ------------------------------------------------------------------
        // Equipping

        /// <summary>Equips the class's starting equipment (state authority).</summary>
        public void EquipDefaults(Data.ClassData classData)
        {
            if (!Object.HasStateAuthority) return;
            defaultsApplied = true;

            for (int i = 0; i < SLOTS; i++) Equip(i, 0);

            if (classData == null || classData.startingEquipment == null) return;
            foreach (var item in classData.startingEquipment)
            {
                if (item != null) Equip(item);
            }
        }

        /// <summary>Equips an item into the slot its data declares. Returns false if not registered / not allowed.</summary>
        public bool Equip(Data.EquipmentData data)
        {
            if (!Object.HasStateAuthority || data == null) return false;
            int id = Registry != null ? Registry.IdOf(data) : 0;
            if (id == 0)
            {
                Debug.LogWarning($"[NetworkLoadout] '{data.name}' is not in the active EquipmentRegistry");
                return false;
            }
            if (!data.IsAllowedFor(owner.ClassType)) return false;
            Equip((int)data.slotType, id);
            return true;
        }

        private void Equip(int slot, int id)
        {
            if (slot < 0 || slot >= SLOTS) return;
            var data = Registry?.Get(id);

            SlotItemIds.Set(slot, id);
            Magazine.Set(slot, data != null && data.UsesAmmo ? data.magazineSize : 0);
            Reserve.Set(slot, data != null && data.UsesAmmo ? data.reserveAmmo : 0);
            UseTimers.Set(slot, TickTimer.None);
            ReloadTimers.Set(slot, TickTimer.None);
        }

        /// <summary>Lobby request from the owning client to swap an item (pre-match only).</summary>
        public void RequestEquip(Data.EquipmentData data)
        {
            if (data == null) return;
            int id = Registry != null ? Registry.IdOf(data) : 0;
            if (id == 0) return;
            if (Object.HasStateAuthority) TryLobbyEquip(id);
            else if (Object.HasInputAuthority) RPC_RequestEquip(id);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_RequestEquip(int id)
        {
            TryLobbyEquip(id);
        }

        private void TryLobbyEquip(int id)
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && !gm.AllowClassChange) return;
            var data = Registry?.Get(id);
            if (data != null) Equip(data);
        }

        public Data.EquipmentData GetData(int slot)
        {
            return slot >= 0 && slot < SLOTS ? Registry?.Get(SlotItemIds[slot]) : null;
        }

        public Data.EquipmentData GetData(EquipmentSlotType slot) => GetData((int)slot);

        // ------------------------------------------------------------------
        // Use / ammo / reload (state authority)

        public bool IsOnCooldown(int slot) => !UseTimers[slot].ExpiredOrNotRunning(Runner);
        public bool IsReloading(int slot) => ReloadTimers[slot].IsRunning && !ReloadTimers[slot].Expired(Runner);
        public float ReloadProgress(int slot)
        {
            var data = GetData(slot);
            if (data == null || !IsReloading(slot) || data.reloadTime <= 0f) return 0f;
            float remaining = ReloadTimers[slot].RemainingTime(Runner) ?? 0f;
            return Mathf.Clamp01(1f - remaining / data.reloadTime);
        }

        public bool CanUse(int slot)
        {
            if (slot < 0 || slot >= SLOTS || SlotItemIds[slot] == 0) return false;
            if (IsOnCooldown(slot) || IsReloading(slot)) return false;
            return true;
        }

        public void StartCooldown(int slot, float seconds)
        {
            if (!Object.HasStateAuthority || seconds <= 0f) return;
            UseTimers.Set(slot, TickTimer.CreateFromSeconds(Runner, seconds));
        }

        /// <summary>Spends one round. Auto-starts a reload when the magazine is empty. Returns false if nothing was spent.</summary>
        public bool ConsumeAmmo(int slot)
        {
            if (!Object.HasStateAuthority) return false;
            var data = GetData(slot);
            if (data == null) return false;
            if (!data.UsesAmmo) return true;

            if (Magazine[slot] <= 0)
            {
                StartReload(slot);
                return false;
            }
            Magazine.Set(slot, Magazine[slot] - 1);
            return true;
        }

        public bool StartReload(int slot)
        {
            if (!Object.HasStateAuthority) return false;
            var data = GetData(slot);
            if (data == null || !data.UsesAmmo) return false;
            if (IsReloading(slot) || Magazine[slot] >= data.magazineSize || Reserve[slot] <= 0) return false;

            ReloadTimers.Set(slot, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, data.reloadTime)));
            RPC_ReloadStarted(slot);
            return true;
        }

        /// <summary>Reloads the first weapon slot that needs it (Primary, then Secondary).</summary>
        public void ReloadAny()
        {
            if (!StartReload((int)EquipmentSlotType.Primary)) StartReload((int)EquipmentSlotType.Secondary);
        }

        private void FinishReload(int slot)
        {
            var data = GetData(slot);
            if (data == null) return;
            int needed = data.magazineSize - Magazine[slot];
            int taken = Mathf.Min(needed, Reserve[slot]);
            Magazine.Set(slot, Magazine[slot] + taken);
            Reserve.Set(slot, Reserve[slot] - taken);
        }

        public void AddReserveAmmo(int slot, int amount)
        {
            if (!Object.HasStateAuthority || amount <= 0) return;
            Reserve.Set(slot, Reserve[slot] + amount);
        }

        /// <summary>Called by weapons after a shot so every peer can play muzzle/impact effects.</summary>
        public void NotifyFired(int slot, Vector3 point, bool hit)
        {
            if (!Object.HasStateAuthority) return;
            var data = GetData(slot);
            if (data != null && data.noiseRadius > 0f) AI.AIBlackboard.ReportNoise(transform.position, data.noiseRadius); // v0.6
            RPC_WeaponFired(slot, point, hit);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_WeaponFired(int slot, Vector3 point, NetworkBool hit)
        {
            WeaponFired?.Invoke(this, slot, point, hit);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ReloadStarted(int slot)
        {
            ReloadStarted?.Invoke(this, slot);
        }

        // ------------------------------------------------------------------
        // Passive modifiers (sum of equipped items' fractions)

        public float DamageTakenMultiplier => Mathf.Clamp(1f - SumModifier(d => d.defenseModifier), 0.1f, 2f);
        public float OutgoingDamageMultiplier => Mathf.Max(0f, 1f + SumModifier(d => d.damageModifier));
        public float SpeedMultiplier => Mathf.Clamp(1f + SumModifier(d => d.speedModifier), 0.3f, 2f);

        private float SumModifier(Func<Data.EquipmentData, float> selector)
        {
            float sum = 0f;
            for (int i = 0; i < SLOTS; i++)
            {
                var data = GetData(i);
                if (data != null) sum += selector(data);
            }
            return sum;
        }
    }
}
