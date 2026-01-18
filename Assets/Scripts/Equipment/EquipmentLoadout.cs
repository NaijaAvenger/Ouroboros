using UnityEngine;
using System.Collections.Generic;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Manages equipment loadout for a player.
    /// Supports modular equipment system with multiple slots.
    /// </summary>
    public class EquipmentLoadout : MonoBehaviour
    {
        [Header("Equipment Slots")]
        [SerializeField] private int maxEquipmentSlots = Core.GameConstants.MAX_EQUIPMENT_SLOTS;
        
        private Dictionary<EquipmentSlotType, IEquipment> equippedItems = new Dictionary<EquipmentSlotType, IEquipment>();
        private List<IEquipment> allEquipment = new List<IEquipment>();
        
        private void Awake()
        {
            InitializeSlots();
        }
        
        private void InitializeSlots()
        {
            // Initialize all possible equipment slots
            equippedItems[EquipmentSlotType.Primary] = null;
            equippedItems[EquipmentSlotType.Secondary] = null;
            equippedItems[EquipmentSlotType.Utility] = null;
            equippedItems[EquipmentSlotType.Gadget] = null;
            equippedItems[EquipmentSlotType.Armor] = null;
            equippedItems[EquipmentSlotType.Accessory] = null;
        }
        
        /// <summary>
        /// Equips an item to the appropriate slot.
        /// </summary>
        public bool EquipItem(IEquipment equipment)
        {
            if (equipment == null) return false;
            
            EquipmentSlotType slot = equipment.SlotType;
            
            // Unequip current item in slot if exists
            if (equippedItems[slot] != null)
            {
                UnequipItem(slot);
            }
            
            // Equip new item
            equippedItems[slot] = equipment;
            equipment.OnEquip();
            allEquipment.Add(equipment);
            
            Debug.Log($"[EquipmentLoadout] Equipped {equipment.EquipmentName} to {slot} slot");
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
            
            Debug.Log($"[EquipmentLoadout] Unequipped {equipment.EquipmentName} from {slot} slot");
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
        /// Uses equipment in a specific slot.
        /// </summary>
        public void UseEquipment(EquipmentSlotType slot)
        {
            IEquipment equipment = GetEquipment(slot);
            if (equipment != null)
            {
                equipment.Use();
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
            foreach (var slot in equippedItems.Keys)
            {
                UnequipItem(slot);
            }
        }
    }
}
