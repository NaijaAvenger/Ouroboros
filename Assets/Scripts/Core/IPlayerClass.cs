using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Interface for all player classes. Designed for modularity and future expansion.
    /// </summary>
    public interface IPlayerClass
    {
        string ClassName { get; }
        string Description { get; }
        
        float MaxHealth { get; }
        float MaxStamina { get; }
        float MovementSpeed { get; }
        
        void Initialize();
        void OnClassSelected();
        void UseAbility(int abilityIndex);
        void UpdateClass(float deltaTime);
    }
}
