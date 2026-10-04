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
        /// <summary>Movement speed while sprinting (v0.2).</summary>
        float SprintSpeed { get; }

        void Initialize();
        /// <summary>Initialise with the owning player's context. Preferred over the parameterless overload (v0.2).</summary>
        void Initialize(IAbilityContext context);
        void OnClassSelected();
        /// <summary>Attempts to use an ability. Returns false if on cooldown, out of stamina, or invalid (v0.2: now returns bool).</summary>
        bool UseAbility(int abilityIndex);
        void UpdateClass(float deltaTime);

        /// <summary>Lets a class scale incoming damage (e.g. Agent shield). Returns the damage to actually apply (v0.2).</summary>
        float ModifyIncomingDamage(float damage);
        /// <summary>Static per-slot definitions for UI / cooldown lookups (v0.2).</summary>
        AbilitySlot GetAbilitySlot(int abilityIndex);
    }

    /// <summary>
    /// Lightweight description of one ability slot. Classes fill these in <c>OnInitialize</c>.
    /// Designer-tunable copies live in <see cref="Data.AbilityData"/>; this is the runtime view.
    /// </summary>
    [System.Serializable]
    public struct AbilitySlot
    {
        public string Name;
        public float Cooldown;
        public float StaminaCost;
        /// <summary>Duration of the effect, 0 for instant abilities.</summary>
        public float Duration;

        public AbilitySlot(string name, float cooldown, float staminaCost, float duration = 0f)
        {
            Name = name;
            Cooldown = cooldown;
            StaminaCost = staminaCost;
            Duration = duration;
        }
    }
}
