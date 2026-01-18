using UnityEngine;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Tech/ability system for special equipment and abilities.
    /// Designed for modularity and future expansion.
    /// </summary>
    public interface ITech
    {
        string TechName { get; }
        string Description { get; }
        float Cooldown { get; }
        float Duration { get; }
        
        bool IsReady();
        void Activate();
        void Deactivate();
    }
    
    /// <summary>
    /// Base abstract class for all tech/special abilities.
    /// </summary>
    public abstract class BaseTech : MonoBehaviour, ITech
    {
        [Header("Tech Info")]
        [SerializeField] protected string techName;
        [SerializeField] protected string description;
        
        [Header("Tech Stats")]
        [SerializeField] protected float cooldown = 30f;
        [SerializeField] protected float duration = 10f;
        [SerializeField] protected int energyCost = 50;
        
        protected float lastActivationTime;
        protected bool isActive;
        
        public string TechName => techName;
        public string Description => description;
        public float Cooldown => cooldown;
        public float Duration => duration;
        
        public virtual bool IsReady()
        {
            return !isActive && (Time.time - lastActivationTime >= cooldown);
        }
        
        public virtual void Activate()
        {
            if (!IsReady()) return;
            
            isActive = true;
            lastActivationTime = Time.time;
            
            OnActivate();
            
            if (duration > 0)
            {
                Invoke(nameof(Deactivate), duration);
            }
        }
        
        public virtual void Deactivate()
        {
            if (!isActive) return;
            
            isActive = false;
            OnDeactivate();
        }
        
        protected abstract void OnActivate();
        protected abstract void OnDeactivate();
    }
}
