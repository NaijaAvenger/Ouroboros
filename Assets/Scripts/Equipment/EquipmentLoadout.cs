using UnityEngine;
using System;
using System.Collections.Generic;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Manages equipment loadout for a player.
    /// Supports modular equipment system with multiple slots.
    ///
    /// v0.4: when a <see cref="NetworkLoadout"/> is present, this component mirrors its replicated slot
    /// ids into local <see cref="BaseEquipment"/> components (one per slot, type chosen by
    /// <see cref="Data.EquipmentKind"/>). Without one it still works as a purely local loadout.
    /// </summary>
    public class EquipmentLoadout : MonoBehaviour
    {
        [Header("Equipment Slots")]
        [SerializeField] private int maxEquipmentSlots = Core.GameConstants.MAX_EQUIPMENT_SLOTS;
        
        private Dictionary<EquipmentSlotType, IEquipment> equippedItems = new Dictionary<EquipmentSlotType, IEquipment>();
        private List<IEquipment> allEquipment = new List<IEquipment>();

        private NetworkLoadout networkLoadout;
        private Network.NetworkPlayer owner;

        /// <summary>Fired when a slot's local component changes (slot, equipment or null).</summary>
        public event Action<EquipmentSlotType, IEquipment> Changed;
        
        private void Awake()
        {
            InitializeSlots();
            owner = GetComponent<Network.NetworkPlayer>();
            networkLoadout = GetComponent<NetworkLoadout>();
            if (networkLoadout != null) networkLoadout.SlotChanged += OnNetworkSlotChanged;
        }

        private void OnDestroy()
        {
            if (networkLoadout != null) networkLoadout.SlotChanged -= OnNetworkSlotChanged;
        }
        
        private void InitializeSlots()
        {
            // [v0.1] six hard-coded assignments; now driven by the enum so new slots are picked up
            foreach (EquipmentSlotType slot in Enum.GetValues(typeof(EquipmentSlotType)))
            {
                equippedItems[slot] = null;
            }
        }

        // ------------------------------------------------------------------
        // Networked path

        private void OnNetworkSlotChanged(int slotIndex, Data.EquipmentData data)
        {
            var slot = (EquipmentSlotType)slotIndex;
            UnequipItem(slot);
            if (data == null) return;

            Type type = ComponentTypeFor(data.kind);
            if (type == null) return;

            var component = (BaseEquipment)gameObject.AddComponent(type);
            component.Initialize(owner, networkLoadout, slotIndex, data);
            EquipItem(component);
        }

        /// <summary>Maps an item kind to its behaviour. Extend here for new families.</summary>
        public static Type ComponentTypeFor(Data.EquipmentKind kind)
        {
            switch (kind)
            {
                case Data.EquipmentKind.HitscanWeapon:    return typeof(HitscanWeapon);
                case Data.EquipmentKind.ProjectileWeapon: return typeof(ProjectileWeapon);
                case Data.EquipmentKind.PassiveGear:      return typeof(PassiveGear);
                case Data.EquipmentKind.Deployable:       return typeof(PassiveGear); // placeholder until deployables exist
                default: return null;
            }
        }

        // ------------------------------------------------------------------
        
        /// <summary>
        /// Equips an item to the appropriate slot.
        /// </summary>
        public bool EquipItem(IEquipment equipment)
        {
            if (equipment == null) return false;
            
            EquipmentSlotType slot = equipment.SlotType;
            
            // Unequip current item in slot if exists (v0.2: tolerate slots added to the enum later)
            if (!equippedItems.ContainsKey(slot)) equippedItems[slot] = null;
            if (equippedItems[slot] != null)
            {
                UnequipItem(slot);
            }
            
            // Equip new item
            equippedItems[slot] = equipment;
            equipment.OnEquip();
            allEquipment.Add(equipment);
            
            Debug.Log($"[EquipmentLoadout] Equipped {equipment.EquipmentName} to {slot} slot");
            Changed?.Invoke(slot, equipment);
            return true;
        }
        
        /// <summary>
        /// Unequips an item from a specific slot.
        /// </summary>
        public bool UnequipItem(EquipmentSlotType slot)
        {
            if (!equippedItems.ContainsKey(slot) || equippedItems[slot] == null)
            {
                return false;
            }
            
            IEquipment equipment = equippedItems[slot];
            equipment.OnUnequip();
            allEquipment.Remove(equipment);
            equippedItems[slot] = null;

            // v0.4: networked items are runtime components; remove them with the slot
            if (equipment is BaseEquipment component && component.IsNetworked)
            {
                Destroy(component);
            }
            
            Debug.Log($"[EquipmentLoadout] Unequipped {equipment.EquipmentName} from {slot} slot");
            Changed?.Invoke(slot, null);
            return true;
        }
        
        /// <summary>
        /// Gets the equipment in a specific slot.
        /// </summary>
        public IEquipment GetEquipment(EquipmentSlotType slot)
        {
            return equippedItems.ContainsKey(slot) ? equippedItems[slot] : null;
        }
        
        /// <summary>
        /// Uses equipment in a specific slot (treated as a press).
        /// </summary>
        public void UseEquipment(EquipmentSlotType slot)
        {
            UseEquipment(slot, true, true);
        }

        /// <summary>v0.4: edge + level aware use so automatic weapons can fire while held.</summary>
        public void UseEquipment(EquipmentSlotType slot, bool pressed, bool held)
        {
            IEquipment equipment = GetEquipment(slot);
            if (equipment != null)
            {
                equipment.Use(pressed, held);
            }
        }
        
        /// <summary>
        /// Gets all equipped items.
        /// </summary>
        public List<IEquipment> GetAllEquippedItems()
        {
            return new List<IEquipment>(allEquipment);
        }
        
        /// <summary>
        /// Clears all equipped items.
        /// </summary>
        public void ClearAllEquipment()
        {
            // [v0.1] foreach (var slot in equippedItems.Keys) { UnequipItem(slot); }
            // [v0.1] BUG: UnequipItem writes equippedItems[slot] while the key collection is being enumerated,
            // [v0.1] which throws InvalidOperationException on Unity's Mono runtime.
            var slots = new List<EquipmentSlotType>(equippedItems.Keys);
            foreach (var slot in slots)
            {
                UnequipItem(slot);
            }
        }
    }
}
