using Fusion;
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
        [SerializeField] private float tacticalShieldDamageMultiplier = 0.5f;
        [SerializeField] private float damageBoostMultiplier = 1.5f;
        [SerializeField] private float damageBoostDuration = 6f;
        [SerializeField] private float reconDroneRange = 20f;
        [SerializeField] private float reconRevealDuration = 6f;
        [SerializeField] private float flashbangRadius = 8f;
        [SerializeField] private float flashbangDuration = 3f;

        private bool hasActiveShield = false;
        private bool hasDamageBoost = false;

        // v0.2: tick-aligned timers instead of Invoke()
        private TickTimer shieldTimer;
        private TickTimer damageBoostTimer;

        public bool HasActiveShield => hasActiveShield;
        public bool HasDamageBoost => hasDamageBoost;
        public float CurrentDamageMultiplier => hasDamageBoost ? damageBoostMultiplier : 1f;
        public override float OutgoingDamageMultiplier => CurrentDamageMultiplier;

        protected override void OnInitialize()
        {
            className = "Agent";
            description = "Versatile field operative with balanced combat capabilities and tactical support options.";

            // Agent has balanced stats across all categories
            maxHealth = 100f;
            movementSpeed = 5.2f;
            sprintSpeed = 7.5f;

            DefineAbility(0, "Tactical Shield", cooldown: 20f, staminaCost: 20f, duration: tacticalShieldDuration);
            DefineAbility(1, "Damage Boost",    cooldown: 18f, staminaCost: 15f, duration: damageBoostDuration);
            DefineAbility(2, "Recon Drone",     cooldown: 25f, staminaCost: 10f, duration: reconRevealDuration);
            DefineAbility(3, "Flashbang",       cooldown: 12f, staminaCost: 10f, duration: flashbangDuration);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return DeployTacticalShield();
                case 1: return ActivateDamageBoost();
                case 2: return DeployReconDrone();
                case 3: return FlashBang();
            }
            return false;
        }

        private bool DeployTacticalShield()
        {
            if (hasActiveShield) return false;

            Log($"Deploying tactical shield for {tacticalShieldDuration} seconds");
            hasActiveShield = true;
            shieldTimer = StartTimer(tacticalShieldDuration);
            SetStatus(Core.StatusFlags.Shielded, true);
            // [v0.1] Invoke(nameof(RemoveShield), tacticalShieldDuration);
            return true;
        }

        private void RemoveShield()
        {
            hasActiveShield = false;
            SetStatus(Core.StatusFlags.Shielded, false);
            Log("Tactical shield expired");
        }

        private bool ActivateDamageBoost()
        {
            if (hasDamageBoost) return false;

            Log($"Activating damage boost: {damageBoostMultiplier}x");
            hasDamageBoost = true;
            damageBoostTimer = StartTimer(damageBoostDuration);
            SetStatus(Core.StatusFlags.DamageBoost, true);
            // [v0.1] Invoke(nameof(RemoveDamageBoost), 6f);
            return true;
        }

        private void RemoveDamageBoost()
        {
            hasDamageBoost = false;
            SetStatus(Core.StatusFlags.DamageBoost, false);
            Log("Damage boost expired");
        }

        private bool DeployReconDrone()
        {
            Log($"Deploying recon drone with {reconDroneRange}m range");
            if (context == null) return true;

            // Reveal every enemy inside range (server-side overlap, status flag replicates to all peers)
            int revealed = 0;
            foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, reconDroneRange, context.Team, aliveOnly: true))
            {
                enemy.ApplyTimedStatus(Core.StatusFlags.Revealed, reconRevealDuration);
                revealed++;
            }
            Log($"Recon drone revealed {revealed} enemies");
            return true;
        }

        private bool FlashBang()
        {
            Log("Throwing flashbang");
            if (context == null) return true;

            // Instant area flash; proper projectile arc can be layered on later.
            Vector3 origin = context.EyePosition + context.AimDirection * 2f;
            foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(origin, flashbangRadius, context.Team, aliveOnly: true))
            {
                enemy.ApplyTimedStatus(Core.StatusFlags.Flashed, flashbangDuration);
            }
            return true;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (hasActiveShield && TimerExpired(shieldTimer)) RemoveShield();
            if (hasDamageBoost && TimerExpired(damageBoostTimer)) RemoveDamageBoost();
        }

        // [v0.1] public override void TakeDamage(float damage)
        // [v0.1] {
        // [v0.1]     float actualDamage = hasActiveShield ? damage * 0.5f : damage;
        // [v0.1]     base.TakeDamage(actualDamage);
        // [v0.1] }
        public override float ModifyIncomingDamage(float damage)
        {
            return hasActiveShield ? damage * tacticalShieldDamageMultiplier : damage;
        }

        protected override void OnDeath()
        {
            if (hasActiveShield) RemoveShield();
            if (hasDamageBoost) RemoveDamageBoost();
        }
    }
}
