using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Agent class - Balanced operative with versatile combat and tactical abilities.
    /// </summary>
    public class AgentClass : Core.BasePlayerClass
    {
        [Header("Agent Abilities")]
        [SerializeField] private float tacticalShieldDuration = 8f;
        [SerializeField] private float damageBoostMultiplier = 1.5f;
        [SerializeField] private float reconDroneRange = 20f;
        
        private bool hasActiveShield = false;
        private bool hasDamageBoost = false;
        
        protected override void OnInitialize()
        {
            className = "Agent";
            description = "Versatile field operative with balanced combat capabilities and tactical support options.";
            
            // Agent has balanced stats across all categories
            maxHealth = 100f;
            movementSpeed = 5.2f;
        }
        
        public override void UseAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0:
                    DeployTacticalShield();
                    break;
                case 1:
                    ActivateDamageBoost();
                    break;
                case 2:
                    DeployReconDrone();
                    break;
                case 3:
                    FlashBang();
                    break;
            }
        }
        
        private void DeployTacticalShield()
        {
            if (!hasActiveShield)
            {
                Debug.Log($"[Agent] Deploying tactical shield for {tacticalShieldDuration} seconds");
                hasActiveShield = true;
                Invoke(nameof(RemoveShield), tacticalShieldDuration);
            }
        }
        
        private void RemoveShield()
        {
            hasActiveShield = false;
            Debug.Log("[Agent] Tactical shield expired");
        }
        
        private void ActivateDamageBoost()
        {
            if (!hasDamageBoost)
            {
                Debug.Log($"[Agent] Activating damage boost: {damageBoostMultiplier}x");
                hasDamageBoost = true;
                Invoke(nameof(RemoveDamageBoost), 6f);
            }
        }
        
        private void RemoveDamageBoost()
        {
            hasDamageBoost = false;
            Debug.Log("[Agent] Damage boost expired");
        }
        
        private void DeployReconDrone()
        {
            Debug.Log($"[Agent] Deploying recon drone with {reconDroneRange}m range");
            // Implementation for drone deployment
        }
        
        private void FlashBang()
        {
            Debug.Log("[Agent] Throwing flashbang");
            // Implementation for flashbang effect
        }
        
        public override void TakeDamage(float damage)
        {
            // Reduce damage if shield is active
            float actualDamage = hasActiveShield ? damage * 0.5f : damage;
            base.TakeDamage(actualDamage);
        }
    }
}
