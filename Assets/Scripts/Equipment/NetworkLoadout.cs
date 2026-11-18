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

        // ---- v0.9 (M1): shots are networked state, not RPCs, so the shooter's client can predict them ----
        /// <summary>Total shots fired by this loadout. Peers compare it with what they have already shown.</summary>
        [Networked] public int FireCount { get; set; }
        [Networked] public int LastShotSlot { get; set; }
        /// <summary>Where the last shot ended (impact point, or the end of its range).</summary>
        [Networked] public Vector3 LastShotPoint { get; set; }
        [Networked] public NetworkBool LastShotHit { get; set; }
        /// <summary>Spread bloom in degrees as of <see cref="BloomTick"/>; it recovers over time (see <see cref="CurrentBloom"/>).</summary>
        [Networked] public float Bloom { get; set; }
        [Networked] public int BloomTick { get; set; }

        // ---- v0.13 (M5): one weapon in hand ----
        /// <summary>Which weapon slot is in hand: Primary (0) or Secondary (1). Only that one fires and reloads.</summary>
        [Networked] public int ActiveSlot { get; set; }
        /// <summary>Runs while the weapon just swapped to is being drawn; it cannot fire or aim until it ends.</summary>
        [Networked] public TickTimer SwapTimer { get; set; }
        /// <summary>Aiming down sights: tighter spread, slower walk, no sprint. Zoom is the owner's view only.</summary>
        [Networked] public NetworkBool Aiming { get; set; }

        // ---- v0.14: gadgets ----
        /// <summary>Runs while a Loot Scanner sweep is showing loot on the owner's HUD.</summary>
        [Networked] public TickTimer ScanTimer { get; set; }

        private Network.NetworkPlayer owner;
        private ChangeDetector changeDetector;
        private int shownActiveSlot;
        private readonly int[] knownIds = new int[SLOTS];
        private bool defaultsApplied;
        private int shownFireCount;
        private readonly bool[] shownReloading = new bool[SLOTS];

        /// <summary>
        /// True where weapon state is simulated: the state authority (truth) and the owning client (prediction).
        /// The client's writes are overwritten by the server's state when it arrives.
        /// </summary>
        public bool CanSimulate => Object != null && (Object.HasStateAuthority || Object.HasInputAuthority);

        /// <summary>Fired on every peer when a slot's item id changes (slot index, new data or null).</summary>
        public event Action<int, Data.EquipmentData> SlotChanged;
        /// <summary>
        /// Fired on every peer when a weapon fires (loadout, slot, world hit/end point, hit something). Cosmetic.
        /// v0.9: raised from <see cref="Render"/> off <see cref="FireCount"/>, immediately on the shooter's own client.
        /// </summary>
        public static event Action<NetworkLoadout, int, Vector3, bool> WeaponFired;
        /// <summary>Fired on every peer when a reload starts (loadout, slot).</summary>
        public static event Action<NetworkLoadout, int> ReloadStarted;
        /// <summary>(v0.13) Fired on every peer when the weapon in hand changes (loadout, new slot). Cosmetic.</summary>
        public static event Action<NetworkLoadout, int> WeaponSwapped;

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

            // Late joiners must not replay shots / reloads that happened before they arrived.
            shownFireCount = FireCount;
            for (int i = 0; i < SLOTS; i++) shownReloading[i] = IsReloading(i);
            shownActiveSlot = ActiveSlot;
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

            RenderShots();
            RenderReloads();

            // (v0.13) predicted on the owner's client, so the swap shows at once
            if (ActiveSlot != shownActiveSlot)
            {
                shownActiveSlot = ActiveSlot;
                WeaponSwapped?.Invoke(this, ActiveSlot);
            }
        }

        /// <summary>
        /// (v0.9) Raises <see cref="WeaponFired"/> for shots this peer has not shown yet. On the shooter's own
        /// client <see cref="FireCount"/> is predicted, so feedback is immediate; everyone else sees it when the
        /// server's state arrives. A rolled-back prediction just lowers the count again.
        /// </summary>
        private void RenderShots()
        {
            int count = FireCount;
            if (count < shownFireCount) { shownFireCount = count; return; }
            if (count == shownFireCount) return;

            int newShots = Mathf.Min(count - shownFireCount, 3); // never spam after a hitch
            shownFireCount = count;
            for (int i = 0; i < newShots; i++)
            {
                WeaponFired?.Invoke(this, LastShotSlot, LastShotPoint, LastShotHit);
            }
        }

        private void RenderReloads()
        {
            for (int i = 0; i < SLOTS; i++)
            {
                bool reloading = IsReloading(i);
                if (reloading && !shownReloading[i]) ReloadStarted?.Invoke(this, i);
                shownReloading[i] = reloading;
            }
        }

        public override void FixedUpdateNetwork()
        {
            // [v0.8] if (!Object.HasStateAuthority) return;   // the owning client now predicts reloads too
            if (!CanSimulate) return;

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
            // (v0.13) a fresh kit starts with the primary in hand
            if (SlotItemIds[(int)EquipmentSlotType.Primary] != 0) ActiveSlot = (int)EquipmentSlotType.Primary;
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

            // (v0.13) never leave an empty hand while the other weapon slot holds something
            if (IsWeaponSlot(slot))
            {
                if (!IsWeaponSlot(ActiveSlot)) ActiveSlot = 0;
                int other = ActiveSlot == 0 ? 1 : 0;
                if (SlotItemIds[ActiveSlot] == 0 && SlotItemIds[other] != 0) ActiveSlot = other;
                Aiming = false;
                SwapTimer = TickTimer.None;
            }
        }

        // ------------------------------------------------------------------
        // The weapon in hand (v0.13)

        /// <summary>Primary and Secondary are the two weapons you hold one at a time.</summary>
        public static bool IsWeaponSlot(int slot) => slot == (int)EquipmentSlotType.Primary || slot == (int)EquipmentSlotType.Secondary;

        public Data.EquipmentData ActiveData => GetData(ActiveSlot);
        public bool IsSwapping => SwapTimer.IsRunning && !SwapTimer.Expired(Runner);

        /// <summary>0 while the new weapon is still coming up, 1 once it is ready. For the view model.</summary>
        public float SwapProgress
        {
            get
            {
                if (!IsSwapping) return 1f;
                var data = ActiveData;
                float total = data != null ? Mathf.Max(0.05f, data.swapSeconds) : 0.4f;
                float remaining = SwapTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(1f - remaining / total);
            }
        }

        /// <summary>Swaps to the other weapon. Returns false if there is none. State authority and owner (prediction).</summary>
        public bool CycleWeapon()
        {
            return SelectWeapon(ActiveSlot == 0 ? 1 : 0);
        }

        /// <summary>Puts the weapon in <paramref name="slot"/> in hand. Cancels a reload in progress on the old one.</summary>
        public bool SelectWeapon(int slot)
        {
            if (!CanSimulate || !IsWeaponSlot(slot) || slot == ActiveSlot) return false;
            if (SlotItemIds[slot] == 0) return false;

            if (IsWeaponSlot(ActiveSlot)) ReloadTimers.Set(ActiveSlot, TickTimer.None); // a reload does not finish in the holster
            ActiveSlot = slot;
            Aiming = false;
            var data = GetData(slot);
            SwapTimer = TickTimer.CreateFromSeconds(Runner, data != null ? Mathf.Max(0.05f, data.swapSeconds) : 0.4f);
            return true;
        }

        /// <summary>
        /// Called every tick with whether the aim button is held. Aiming needs a ready hitscan weapon in hand:
        /// not while it is being drawn or reloaded, and not while the player is out of the fight.
        /// </summary>
        public void SetAiming(bool wants)
        {
            if (!CanSimulate) return;
            var data = ActiveData;
            bool can = wants
                       && data != null && data.kind == Data.EquipmentKind.HitscanWeapon
                       && !IsSwapping && !IsReloading(ActiveSlot)
                       && owner != null && owner.IsActiveInMatch
                       && !owner.HasStatus(Core.StatusFlags.Climbing) && !owner.HasStatus(Core.StatusFlags.Grappling);
            if (Aiming != can) Aiming = can;
        }

        /// <summary>Walking speed multiplier from aiming: 1 from the hip.</summary>
        public float AimSpeedMultiplier
        {
            get
            {
                if (!Aiming) return 1f;
                var data = ActiveData;
                return data != null ? Mathf.Clamp(data.adsMoveMultiplier, 0.2f, 1f) : 1f;
            }
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
            // (v0.13) of the two weapons only the one in hand can be used, and not while it is being drawn
            if (IsWeaponSlot(slot) && (slot != ActiveSlot || IsSwapping)) return false;
            return true;
        }

        public void StartCooldown(int slot, float seconds)
        {
            // [v0.8] if (!Object.HasStateAuthority || seconds <= 0f) return;
            if (!CanSimulate || seconds <= 0f) return;
            UseTimers.Set(slot, TickTimer.CreateFromSeconds(Runner, seconds));
        }

        /// <summary>
        /// (v0.12) A rival's jam: every slot refuses to fire for <paramref name="seconds"/>. State authority only.
        /// </summary>
        public void Jam(float seconds)
        {
            if (!Object.HasStateAuthority || seconds <= 0f) return;
            for (int i = 0; i < SLOTS; i++)
            {
                if (SlotItemIds[i] == 0) continue;
                float remaining = UseTimers[i].RemainingTime(Runner) ?? 0f;
                if (seconds > remaining) UseTimers.Set(i, TickTimer.CreateFromSeconds(Runner, seconds));
            }
        }

        // ------------------------------------------------------------------
        // Gadgets (v0.14)

        /// <summary>Fired on every peer when a gadget goes off (loadout, type, world point, reach). Cosmetic.</summary>
        public static event Action<NetworkLoadout, Data.GadgetType, Vector3, float> GadgetUsed;

        public bool IsScanning => Object != null && ScanTimer.IsRunning && !ScanTimer.Expired(Runner);

        public void StartScan(float seconds)
        {
            if (!Object.HasStateAuthority || seconds <= 0f) return;
            ScanTimer = TickTimer.CreateFromSeconds(Runner, seconds);
        }

        /// <summary>True when any slot holds a gadget of this type.</summary>
        public bool HasGadget(Data.GadgetType type)
        {
            if (Object == null) return false;
            for (int i = 0; i < SLOTS; i++)
            {
                var data = GetData(i);
                if (data != null && data.kind == Data.EquipmentKind.Gadget && data.gadget == type) return true;
            }
            return false;
        }

        /// <summary>The gadget in the Gadget slot, or null.</summary>
        public Data.EquipmentData GadgetData
        {
            get
            {
                var data = GetData((int)EquipmentSlotType.Gadget);
                return data != null && data.kind == Data.EquipmentKind.Gadget ? data : null;
            }
        }

        /// <summary>Seconds until the item in <paramref name="slot"/> can be used again. 0 when ready.</summary>
        public float CooldownRemaining(int slot)
        {
            if (Object == null || slot < 0 || slot >= SLOTS) return 0f;
            return UseTimers[slot].IsRunning ? Mathf.Max(0f, UseTimers[slot].RemainingTime(Runner) ?? 0f) : 0f;
        }

        public void NotifyGadget(Data.GadgetType type, Vector3 point, float reach)
        {
            if (!Object.HasStateAuthority) return;
            RPC_GadgetUsed((byte)type, point, reach);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_GadgetUsed(byte type, Vector3 point, float reach)
        {
            GadgetUsed?.Invoke(this, (Data.GadgetType)type, point, reach);
        }

        /// <summary>Spends one round. Auto-starts a reload when the magazine is empty. Returns false if nothing was spent.</summary>
        public bool ConsumeAmmo(int slot)
        {
            // [v0.8] if (!Object.HasStateAuthority) return false;
            if (!CanSimulate) return false;
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
            // [v0.8] if (!Object.HasStateAuthority) return false;
            if (!CanSimulate) return false;
            var data = GetData(slot);
            if (data == null || !data.UsesAmmo) return false;
            if (IsReloading(slot) || Magazine[slot] >= data.magazineSize || Reserve[slot] <= 0) return false;

            ReloadTimers.Set(slot, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, data.reloadTime)));
            // [v0.8] RPC_ReloadStarted(slot);   // Render() now raises ReloadStarted from the replicated timer
            return true;
        }

        /// <summary>Reloads the first weapon slot that needs it (Primary, then Secondary).</summary>
        public void ReloadAny()
        {
            // [v0.12] if (!StartReload((int)EquipmentSlotType.Primary)) StartReload((int)EquipmentSlotType.Secondary);
            ReloadActive(); // (v0.13) only the weapon in hand reloads
        }

        /// <summary>(v0.13) Reloads the weapon in hand. Aiming drops for the reload.</summary>
        public bool ReloadActive()
        {
            if (!IsWeaponSlot(ActiveSlot) || IsSwapping) return false;
            if (!StartReload(ActiveSlot)) return false;
            Aiming = false;
            return true;
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

        /// <summary>
        /// (v0.9) Records a shot in networked state: bumps <see cref="FireCount"/> (which drives feedback on every
        /// peer), stores where it ended, and raises the bloom. Runs on the state authority and, as prediction, on
        /// the owning client. Compute the shot's spread <b>before</b> calling this.
        /// </summary>
        public void RegisterShot(int slot, Vector3 endPoint, bool hit)
        {
            if (!CanSimulate) return;
            var data = GetData(slot);
            if (data != null)
            {
                Bloom = Core.CombatRules.BloomAfterShot(CurrentBloom, data.bloomPerShot, data.bloomMax);
                BloomTick = (int)Runner.Tick;
            }
            LastShotSlot = slot;
            LastShotPoint = endPoint;
            LastShotHit = hit;
            FireCount++;

            // Guards only hear shots that really happened.
            if (Object.HasStateAuthority && data != null && data.noiseRadius > 0f)
            {
                AI.AIBlackboard.ReportNoise(transform.position, data.noiseRadius); // v0.6
            }
        }

        /// <summary>Legacy name for <see cref="RegisterShot"/>.</summary>
        public void NotifyFired(int slot, Vector3 point, bool hit)
        {
            RegisterShot(slot, point, hit);
            // [v0.8] if (!Object.HasStateAuthority) return;
            // [v0.8] var data = GetData(slot);
            // [v0.8] if (data != null && data.noiseRadius > 0f) AI.AIBlackboard.ReportNoise(transform.position, data.noiseRadius); // v0.6
            // [v0.8] RPC_WeaponFired(slot, point, hit);
        }

        // [v0.8] Shots and reloads were announced with RPCs. An RPC only leaves the server, so the shooter saw
        // [v0.8] their own muzzle flash one round trip late. FireCount / ReloadTimers + Render() replace them.
        // [v0.8] [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        // [v0.8] private void RPC_WeaponFired(int slot, Vector3 point, NetworkBool hit)
        // [v0.8] {
        // [v0.8]     WeaponFired?.Invoke(this, slot, point, hit);
        // [v0.8] }
        // [v0.8] [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        // [v0.8] private void RPC_ReloadStarted(int slot)
        // [v0.8] {
        // [v0.8]     ReloadStarted?.Invoke(this, slot);
        // [v0.8] }

        // ------------------------------------------------------------------
        // Spread (v0.9)

        /// <summary>Bloom in degrees right now: what the last shot left, minus recovery since then.</summary>
        public float CurrentBloom
        {
            get
            {
                if (Bloom <= 0f || Runner == null) return 0f;
                var data = GetData(LastShotSlot);
                float recovery = data != null ? data.bloomRecoveryPerSecond : 6f;
                float seconds = Mathf.Max(0, (int)Runner.Tick - BloomTick) * Runner.DeltaTime;
                return Core.CombatRules.DecayedBloom(Bloom, seconds, recovery);
            }
        }

        /// <summary>Cone half-angle in degrees the next shot from <paramref name="slot"/> would use.</summary>
        public float CurrentSpread(int slot)
        {
            var data = GetData(slot);
            if (data == null) return 0f;
            var controller = owner != null ? owner.Controller : null;
            bool grounded = controller == null || controller.IsGrounded;
            float stance = Core.CombatRules.StanceSpreadMultiplier(grounded,
                owner != null && owner.HasStatus(Core.StatusFlags.Sprinting),
                owner != null && owner.HasStatus(Core.StatusFlags.Sliding));
            // [v0.12] return Core.CombatRules.EffectiveSpread(data.spreadDegrees, CurrentBloom, stance);
            float spread = Core.CombatRules.EffectiveSpread(data.spreadDegrees, CurrentBloom, stance);
            return Core.CombatRules.AimedSpread(spread, Aiming && slot == ActiveSlot, data.adsSpreadMultiplier); // (v0.13)
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
