using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Abstract base class for all player classes.
    /// Provides common functionality and enforces structure for modularity.
    /// </summary>
    public abstract class BasePlayerClass : MonoBehaviour, IPlayerClass
    {
        [Header("Class Information")]
        [SerializeField] protected string className;
        [SerializeField] protected string description;
        
        [Header("Base Stats")]
        [SerializeField] protected float maxHealth = 100f;
        [SerializeField] protected float maxStamina = 100f;
        [SerializeField] protected float movementSpeed = 5f;
        
        protected float currentHealth;
        protected float currentStamina;
        protected bool isInitialized;
        
        public string ClassName => className;
        public string Description => description;
        public float MaxHealth => maxHealth;
        public float MaxStamina => maxStamina;
        public float MovementSpeed => movementSpeed;
        
        public virtual void Initialize()
        {
            if (isInitialized) return;
            
            currentHealth = maxHealth;
            currentStamina = maxStamina;
            isInitialized = true;
            
            OnInitialize();
        }
        
        public virtual void OnClassSelected()
        {
            OnClassEquipped();
        }
        
        public abstract void UseAbility(int abilityIndex);
        
        public virtual void UpdateClass(float deltaTime)
        {
            if (!isInitialized) return;
            
            RegenerateStamina(deltaTime);
            OnUpdate(deltaTime);
        }
        
        protected virtual void RegenerateStamina(float deltaTime)
        {
            if (currentStamina < maxStamina)
            {
                currentStamina = Mathf.Min(currentStamina + (10f * deltaTime), maxStamina);
            }
        }
        
        protected virtual void OnInitialize() { }
        protected virtual void OnClassEquipped() { }
        protected virtual void OnUpdate(float deltaTime) { }
        
        public virtual void TakeDamage(float damage)
        {
            currentHealth = Mathf.Max(0, currentHealth - damage);
            if (currentHealth <= 0)
            {
                OnDeath();
            }
        }
        
        protected virtual void OnDeath() { }
    }
}
