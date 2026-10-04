using UnityEngine;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Base interface for all equipment items. Designed for modularity and easy expansion.
    /// </summary>
    public interface IEquipment
    {
        string EquipmentName { get; }
        string Description { get; }
        EquipmentSlotType SlotType { get; }

        void OnEquip();
        void OnUnequip();
        void Use();
        /// <summary>v0.4: edge + level aware use (automatic weapons use <paramref name="held"/>).</summary>
        void Use(bool pressed, bool held);
    }

    /// <summary>
    /// Equipment slot types for organizing gear. The integer value is the slot index in <see cref="NetworkLoadout"/>.
    /// </summary>
    public enum EquipmentSlotType
    {
        Primary,
        Secondary,
        Utility,
        Gadget,
        Armor,
        Accessory
    }

    /// <summary>
    /// Base abstract class for all equipment.
    ///
    /// v0.4: equipment is a local MonoBehaviour created on every peer from the replicated
    /// <see cref="NetworkLoadout"/> slot ids, configured by <see cref="Data.EquipmentData"/>. Cooldowns and
    /// ammo are replicated through the loadout; <see cref="OnUse"/> runs only on the state authority.
    /// </summary>
    public abstract class BaseEquipment : MonoBehaviour, IEquipment
    {
        [Header("Equipment Info")]
        [SerializeField] protected string equipmentName;
        [SerializeField] protected string description;
        [SerializeField] protected EquipmentSlotType slotType;

        [Header("Equipment Stats")]
        [SerializeField] protected float cooldown = 5f;
        [SerializeField] protected int maxUses = -1; // -1 for unlimited

        // [v0.1] protected float lastUseTime;   // superseded by NetworkLoadout.UseTimers (tick timers)
        protected int currentUses;
        protected bool isEquipped;

        protected Network.NetworkPlayer owner;
        protected NetworkLoadout loadout;
        protected Data.EquipmentData data;
        protected int slotIndex = -1;

        public string EquipmentName => equipmentName;
        public string Description => description;
        public EquipmentSlotType SlotType => slotType;
        public Data.EquipmentData Data => data;
        public int SlotIndex => slotIndex;
        public bool IsNetworked => loadout != null && slotIndex >= 0;

        protected virtual void Awake()
        {
            currentUses = maxUses;
        }

        /// <summary>Binds this component to its owner and data. Called by <see cref="EquipmentLoadout"/> right after AddComponent.</summary>
        public virtual void Initialize(Network.NetworkPlayer player, NetworkLoadout networkLoadout, int slot, Data.EquipmentData equipmentData)
        {
            owner = player;
            loadout = networkLoadout;
            slotIndex = slot;
            data = equipmentData;

            if (data != null)
            {
                equipmentName = data.equipmentName;
                description = data.description;
                slotType = data.slotType;
                cooldown = data.cooldown;
                maxUses = data.maxUses;
                currentUses = maxUses;
            }
            OnInitialized();
        }

        public virtual void OnEquip()
        {
            isEquipped = true;
            OnEquipped();
        }

        public virtual void OnUnequip()
        {
            isEquipped = false;
            OnUnequipped();
        }

        /// <summary>Legacy single-shot use (treated as a press).</summary>
        public virtual void Use()
        {
            Use(true, true);
        }

        public virtual void Use(bool pressed, bool held)
        {
            bool wants = data != null && data.automatic ? held : pressed;
            if (!wants) return;
            if (!CanUse()) return;

            // [v0.1] OnUse(); lastUseTime = Time.time; if (maxUses > 0) currentUses--;
            if (!OnUse()) return;

            if (IsNetworked) loadout.StartCooldown(slotIndex, cooldown);
            if (maxUses > 0) currentUses--;
        }

        protected virtual bool CanUse()
        {
            if (!isEquipped) return false;
            if (maxUses > 0 && currentUses <= 0) return false;

            if (IsNetworked)
            {
                if (owner == null || owner.Object == null || !owner.Object.HasStateAuthority) return false;
                if (!owner.IsActiveInMatch) return false;
                if (!loadout.CanUse(slotIndex)) return false;
            }
            // [v0.1] if (Time.time - lastUseTime < cooldown) return false;  // non-networked items have no cooldown now
            return true;
        }

        protected Fusion.NetworkRunner Runner => owner != null ? owner.Runner : null;

        protected virtual void OnInitialized() { }
        protected virtual void OnEquipped() { }
        protected virtual void OnUnequipped() { }
        /// <summary>Perform the effect. Return false if nothing happened (no cooldown / use consumed).</summary>
        protected virtual bool OnUse() { return true; }
    }
}
