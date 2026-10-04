using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Abstract base class for all player classes.
    /// Provides common functionality and enforces structure for modularity.
    ///
    /// v0.2: Health and stamina are owned by <c>NetworkPlayer</c> (the single networked source of
    /// truth). The class only supplies stats, ability definitions and ability behaviour, and talks to
    /// its owner through <see cref="IAbilityContext"/>. All timing uses Fusion tick timers so results
    /// are identical on every peer and survive resimulation.
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
        [SerializeField] protected float sprintSpeed = 7f;

        // [v0.1] protected float currentHealth;   // superseded: NetworkPlayer.Health is the source of truth
        // [v0.1] protected float currentStamina;  // superseded: NetworkPlayer.Stamina is the source of truth
        protected bool isInitialized;

        /// <summary>Owner-provided context. Null until <see cref="Initialize(IAbilityContext)"/> runs.</summary>
        protected IAbilityContext context;

        /// <summary>Designer data applied via <see cref="ApplyClassData"/>; null when running on code defaults.</summary>
        protected Data.ClassData classData;

        /// <summary>Per-slot ability definitions. Filled by subclasses in <see cref="OnInitialize"/>.</summary>
        protected readonly AbilitySlot[] abilitySlots = new AbilitySlot[GameConstants.MAX_ABILITY_SLOTS];

        public string ClassName => className;
        public string Description => description;
        public float MaxHealth => maxHealth;
        public float MaxStamina => maxStamina;
        public float MovementSpeed => movementSpeed;
        public float SprintSpeed => sprintSpeed;
        public IAbilityContext Context => context;
        public Data.ClassData ClassData => classData;

        public virtual void Initialize()
        {
            if (isInitialized) return;

            // [v0.1] currentHealth = maxHealth;
            // [v0.1] currentStamina = maxStamina;
            isInitialized = true;

            OnInitialize();
        }

        public virtual void Initialize(IAbilityContext abilityContext)
        {
            context = abilityContext;
            Initialize();
        }

        public virtual void OnClassSelected()
        {
            OnClassEquipped();
        }

        /// <summary>
        /// Overrides code defaults with designer values. Call after <see cref="Initialize()"/> (slots must
        /// exist) and before the owner reads <see cref="MaxHealth"/>. Zero / empty fields are ignored.
        /// </summary>
        public virtual void ApplyClassData(Data.ClassData data)
        {
            if (data == null) return;
            classData = data;

            if (!string.IsNullOrEmpty(data.className)) className = data.className;
            if (!string.IsNullOrEmpty(data.description)) description = data.description;
            if (data.maxHealth > 0f) maxHealth = data.maxHealth;
            if (data.maxStamina > 0f) maxStamina = data.maxStamina;
            if (data.movementSpeed > 0f) movementSpeed = data.movementSpeed;
            if (data.sprintSpeed > 0f) sprintSpeed = data.sprintSpeed;

            if (data.abilities != null)
            {
                int count = Mathf.Min(data.abilities.Length, abilitySlots.Length);
                for (int i = 0; i < count; i++)
                {
                    var a = data.abilities[i];
                    if (a == null) continue;
                    AbilitySlot slot = abilitySlots[i];
                    if (!string.IsNullOrEmpty(a.abilityName)) slot.Name = a.abilityName;
                    if (a.cooldown > 0f) slot.Cooldown = a.cooldown;
                    if (a.energyCost > 0f) slot.StaminaCost = a.energyCost;
                    if (a.duration > 0f) slot.Duration = a.duration;
                    abilitySlots[i] = slot;
                }
            }

            OnClassDataApplied(data);
        }

        /// <summary>Hook for classes that read extra fields (prefabs, modifiers) from their data.</summary>
        protected virtual void OnClassDataApplied(Data.ClassData data) { }

        /// <summary>
        /// Gate-keeps ability use (slot validity, cooldown, stamina) and then defers to
        /// <see cref="ExecuteAbility"/>. Only meaningful on the state authority.
        /// </summary>
        public virtual bool UseAbility(int abilityIndex)
        {
            if (!isInitialized) return false;
            if (abilityIndex < 0 || abilityIndex >= GameConstants.MAX_ABILITY_SLOTS) return false;

            AbilitySlot slot = abilitySlots[abilityIndex];
            if (string.IsNullOrEmpty(slot.Name)) return false;

            if (context != null)
            {
                if (!context.IsAlive) return false;
                if (!context.IsAbilityReady(abilityIndex))
                {
                    return false;
                }
                if (slot.StaminaCost > 0f && !context.TrySpendStamina(slot.StaminaCost))
                {
                    return false;
                }
            }

            bool executed = ExecuteAbility(abilityIndex);

            if (executed && context != null && slot.Cooldown > 0f)
            {
                context.StartAbilityCooldown(abilityIndex, slot.Cooldown);
            }

            return executed;
        }

        /// <summary>Subclasses implement the actual ability effect. Return false to refund the cooldown.</summary>
        protected abstract bool ExecuteAbility(int abilityIndex);

        public virtual void UpdateClass(float deltaTime)
        {
            if (!isInitialized) return;

            // [v0.1] RegenerateStamina(deltaTime); // superseded: NetworkPlayer regenerates stamina
            OnUpdate(deltaTime);
        }

        public virtual float ModifyIncomingDamage(float damage)
        {
            return damage;
        }

        /// <summary>Multiplier applied to damage this player deals (e.g. Agent damage boost).</summary>
        public virtual float OutgoingDamageMultiplier => 1f;

        public AbilitySlot GetAbilitySlot(int abilityIndex)
        {
            if (abilityIndex < 0 || abilityIndex >= abilitySlots.Length) return default;
            return abilitySlots[abilityIndex];
        }

        /// <summary>Helper for subclasses to register a slot in <see cref="OnInitialize"/>.</summary>
        protected void DefineAbility(int slot, string name, float cooldown, float staminaCost, float duration = 0f)
        {
            if (slot < 0 || slot >= abilitySlots.Length) return;
            abilitySlots[slot] = new AbilitySlot(name, cooldown, staminaCost, duration);
        }

        /// <summary>Starts a tick timer through the context, or returns <c>TickTimer.None</c> if there is no context.</summary>
        protected Fusion.TickTimer StartTimer(float seconds)
        {
            return context != null ? context.CreateTimer(seconds) : Fusion.TickTimer.None;
        }

        protected bool TimerExpired(Fusion.TickTimer timer)
        {
            return context == null || context.TimerExpired(timer);
        }

        protected void SetStatus(StatusFlags flag, bool enabled)
        {
            context?.SetStatus(flag, enabled);
        }

        protected void Log(string message)
        {
            Debug.Log($"[{className}] {message}");
        }

        // [v0.1] protected virtual void RegenerateStamina(float deltaTime)
        // [v0.1] {
        // [v0.1]     if (currentStamina < maxStamina)
        // [v0.1]     {
        // [v0.1]         currentStamina = Mathf.Min(currentStamina + (10f * deltaTime), maxStamina);
        // [v0.1]     }
        // [v0.1] }

        protected virtual void OnInitialize() { }
        protected virtual void OnClassEquipped() { }
        protected virtual void OnUpdate(float deltaTime) { }

        // [v0.1] public virtual void TakeDamage(float damage)
        // [v0.1] {
        // [v0.1]     currentHealth = Mathf.Max(0, currentHealth - damage);
        // [v0.1]     if (currentHealth <= 0)
        // [v0.1]     {
        // [v0.1]         OnDeath();
        // [v0.1]     }
        // [v0.1] }

        /// <summary>Called by the owner when the player dies. Classes clear timed effects here.</summary>
        public virtual void OnOwnerDied()
        {
            OnDeath();
        }

        /// <summary>Called by the owner when the player respawns.</summary>
        public virtual void OnOwnerRespawned() { }

        protected virtual void OnDeath() { }
    }
}
