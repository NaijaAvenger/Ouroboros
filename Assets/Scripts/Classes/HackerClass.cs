using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Hacker class - Specializes in electronic warfare, system infiltration, and tech manipulation.
    /// </summary>
    public class HackerClass : Core.BasePlayerClass
    {
        [Header("Hacker Abilities")]
        [SerializeField] private float hackRange = 10f;
        [SerializeField] private float hackDuration = 5f;
        [SerializeField] private float abilityDMGBoost = 1.2f;
        
        private float lastHackTime;
        private const float HACK_COOLDOWN = 15f;
        
        protected override void OnInitialize()
        {
            className = "Hacker";
            description = "Electronic warfare specialist capable of disabling security systems and hacking enemy equipment.";
            
            // Hacker has lower health but higher movement speed
            maxHealth = 85f;
            movementSpeed = 5.5f;
        }
        
        public override void UseAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0:
                    HackSystem();
                    break;
                case 1:
                    DisableSecurityCamera();
                    break;
                case 2:
                    EMPBlast();
                    break;
                case 3:
                    DataMine();
                    break;
            }
        }
        
        private void HackSystem()
        {
            if (Time.time - lastHackTime < HACK_COOLDOWN) return;
            
            Debug.Log($"[Hacker] Initiating system hack with range {hackRange}m");
            lastHackTime = Time.time;
            // Implementation for hacking nearby systems
        }
        
        private void DisableSecurityCamera()
        {
            Debug.Log("[Hacker] Disabling security cameras in area");
            // Implementation for camera disabling
        }
        
        private void EMPBlast()
        {
            Debug.Log("[Hacker] Deploying EMP blast");
            // Implementation for EMP effect
        }
        
        private void DataMine()
        {
            Debug.Log("[Hacker] Data mining enemy positions");
            // Implementation for revealing enemy locations
        }
    }
}
