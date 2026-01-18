using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// ScriptableObject for equipment configuration.
    /// Allows designers to create and modify equipment data without code changes.
    /// </summary>
    [CreateAssetMenu(fileName = "New Equipment", menuName = "Ouroboros/Equipment Data")]
    public class EquipmentData : ScriptableObject
    {
        [Header("Equipment Information")]
        public string equipmentName;
        [TextArea(2, 4)]
        public string description;
        public Sprite equipmentIcon;
        public GameObject equipmentPrefab;
        
        [Header("Equipment Type")]
        public Equipment.EquipmentSlotType slotType;
        public EquipmentRarity rarity;
        
        [Header("Equipment Stats")]
        public float cooldown = 5f;
        public int maxUses = -1; // -1 for unlimited
        public float range = 10f;
        public float effectDuration = 0f;
        
        [Header("Stat Modifiers")]
        public float damageModifier = 0f;
        public float defenseModifier = 0f;
        public float speedModifier = 0f;
        public float stealthModifier = 0f;
        
        [Header("Requirements")]
        public int requiredLevel = 1;
        public Core.PlayerClassType[] allowedClasses; // Empty = all classes
    }
    
    /// <summary>
    /// Equipment rarity levels.
    /// </summary>
    public enum EquipmentRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }
}
