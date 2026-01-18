using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Saboteur class - Specializes in stealth, traps, and environmental manipulation.
    /// </summary>
    public class SaboteurClass : Core.BasePlayerClass
    {
        [Header("Saboteur Abilities")]
        [SerializeField] private float trapPlacementRange = 5f;
        [SerializeField] private int maxTraps = 3;
        [SerializeField] private float stealthDuration = 10f;
        
        private int currentTrapsPlaced = 0;
        private bool isInStealth = false;
        
        protected override void OnInitialize()
        {
            className = "Saboteur";
            description = "Stealth operative skilled in setting traps, sabotaging equipment, and silent elimination.";
            
            // Saboteur has balanced stats with high stealth capabilities
            maxHealth = 90f;
            movementSpeed = 6f;
        }
        
        public override void UseAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0:
                    PlaceTrap();
                    break;
                case 1:
                    ActivateStealth();
                    break;
                case 2:
                    SabotageEquipment();
                    break;
                case 3:
                    SmokeBomb();
                    break;
            }
        }
        
        private void PlaceTrap()
        {
            if (currentTrapsPlaced >= maxTraps)
            {
                Debug.Log("[Saboteur] Maximum traps already placed");
                return;
            }
            
            Debug.Log($"[Saboteur] Placing trap {currentTrapsPlaced + 1}/{maxTraps}");
            currentTrapsPlaced++;
            // Implementation for trap placement
        }
        
        private void ActivateStealth()
        {
            if (!isInStealth)
            {
                Debug.Log($"[Saboteur] Activating stealth for {stealthDuration} seconds");
                isInStealth = true;
                Invoke(nameof(DeactivateStealth), stealthDuration);
            }
        }
        
        private void DeactivateStealth()
        {
            isInStealth = false;
            Debug.Log("[Saboteur] Stealth deactivated");
        }
        
        private void SabotageEquipment()
        {
            Debug.Log("[Saboteur] Sabotaging nearby equipment");
            // Implementation for equipment sabotage
        }
        
        private void SmokeBomb()
        {
            Debug.Log("[Saboteur] Deploying smoke bomb");
            // Implementation for smoke bomb
        }
    }
}
