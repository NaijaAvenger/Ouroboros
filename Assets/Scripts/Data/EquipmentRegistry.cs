using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// Ordered list of every <see cref="EquipmentData"/> the game can equip. Items are replicated as
    /// <c>index + 1</c> (0 means "empty slot"), so the order must be identical on every peer: ship the
    /// same asset and never reorder it mid-session. Published as <see cref="Active"/> by
    /// <c>GameSessionManager</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "Equipment Registry", menuName = "Ouroboros/Equipment Registry")]
    public class EquipmentRegistry : ScriptableObject
    {
        public static EquipmentRegistry Active { get; set; }

        public EquipmentData[] items;

        public int Count => items != null ? items.Length : 0;

        /// <summary>Network id for an item (index + 1), or 0 if not registered.</summary>
        public int IdOf(EquipmentData data)
        {
            if (items == null || data == null) return 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == data) return i + 1;
            }
            return 0;
        }

        /// <summary>Item for a network id, or null for 0 / unknown.</summary>
        public EquipmentData Get(int id)
        {
            int index = id - 1;
            return items != null && index >= 0 && index < items.Length ? items[index] : null;
        }

        /// <summary>All items that fit a slot and are allowed for a class (lobby pickers).</summary>
        public List<EquipmentData> Options(Equipment.EquipmentSlotType slot, Core.PlayerClassType classType)
        {
            var result = new List<EquipmentData>();
            if (items == null) return result;
            foreach (var item in items)
            {
                if (item != null && item.slotType == slot && item.IsAllowedFor(classType)) result.Add(item);
            }
            return result;
        }
    }
}
