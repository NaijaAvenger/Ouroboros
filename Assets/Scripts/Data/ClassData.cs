using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// ScriptableObject for player class configuration.
    /// Allows designers to create and modify class data without code changes.
    ///
    /// v0.3: applied at runtime through <see cref="ClassRegistry"/> → <c>BasePlayerClass.ApplyClassData</c>.
    /// Zero / empty values mean "keep the code default", so partial assets are fine.
    /// </summary>
    [CreateAssetMenu(fileName = "New Class Data", menuName = "Ouroboros/Class Data")]
    public class ClassData : ScriptableObject
    {
        [Header("Class Information")]
        public string className;
        [TextArea(3, 5)]
        public string description;
        public Core.PlayerClassType classType;
        public Sprite classIcon;
        
        [Header("Base Stats")]
        public float maxHealth = 100f;
        public float maxStamina = 100f;
        public float movementSpeed = 5f;
        public float sprintSpeed = 7f;
        
        [Header("Ability Configuration")]
        public AbilityData[] abilities;
        
        [Header("Starting Equipment")]
        public EquipmentData[] startingEquipment;
        
        [Header("Class Modifiers")]
        public float damageMultiplier = 1f;
        public float defenseMultiplier = 1f;
        public float stealthMultiplier = 1f;
        public float hackingMultiplier = 1f;

        [Header("Class Prefabs (v0.3)")]
        [Tooltip("Networked prefab the Saboteur spawns for Place Trap. Runtime-added class components cannot hold their own prefab references, so class prefabs live here.")]
        public Fusion.NetworkObject trapPrefab;
        [Tooltip("Any additional networked prefabs a class may spawn (projectiles, drones, deployables).")]
        public Fusion.NetworkObject[] extraPrefabs;

        /// <summary>Convenience accessor for <see cref="extraPrefabs"/> by index; null when out of range.</summary>
        public Fusion.NetworkObject GetExtraPrefab(int index)
        {
            return extraPrefabs != null && index >= 0 && index < extraPrefabs.Length ? extraPrefabs[index] : null;
        }
    }
    
    /// <summary>
    /// Data structure for ability configuration.
    /// </summary>
    [System.Serializable]
    public class AbilityData
    {
        public string abilityName;
        [TextArea(2, 4)]
        public string description;
        public Sprite abilityIcon;
        public float cooldown;
        public float duration;
        public float energyCost;
        public AbilityEffectType effectType;
    }
    
    /// <summary>
    /// Types of ability effects.
    /// </summary>
    public enum AbilityEffectType
    {
        Damage,
        Heal,
        Buff,
        Debuff,
        Utility,
        Stealth,
        Hack,
        Trap
    }
}
