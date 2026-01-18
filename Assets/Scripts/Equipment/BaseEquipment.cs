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
    }
    
    /// <summary>
    /// Equipment slot types for organizing gear.
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
        
        protected float lastUseTime;
        protected int currentUses;
        protected bool isEquipped;
        
        public string EquipmentName => equipmentName;
        public string Description => description;
        public EquipmentSlotType SlotType => slotType;
        
        protected virtual void Awake()
        {
            currentUses = maxUses;
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
        
        public virtual void Use()
        {
            if (!CanUse()) return;
            
            OnUse();
            lastUseTime = Time.time;
            
            if (maxUses > 0)
            {
                currentUses--;
            }
        }
        
        protected virtual bool CanUse()
        {
            if (!isEquipped) return false;
            if (Time.time - lastUseTime < cooldown) return false;
            if (maxUses > 0 && currentUses <= 0) return false;
            
            return true;
        }
        
        protected virtual void OnEquipped() { }
        protected virtual void OnUnequipped() { }
        protected virtual void OnUse() { }
    }
}
