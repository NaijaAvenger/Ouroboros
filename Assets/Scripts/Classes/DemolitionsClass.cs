using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Demolitions class - Specializes in explosives, breaching, and area denial.
    /// </summary>
    public class DemolitionsClass : Core.BasePlayerClass
    {
        [Header("Demolitions Abilities")]
        [SerializeField] private float explosiveRadius = 8f;
        [SerializeField] private float explosiveDamage = 75f;
        [SerializeField] private int maxExplosives = 4;
        [SerializeField] private float explosiveRegenSeconds = 20f;
        [SerializeField] private float breachRange = 3f;
        [SerializeField] private float incendiaryRadius = 5f;
        [SerializeField] private float incendiaryDuration = 5f;
        [SerializeField] private float incendiaryDamagePerSecond = 8f;

        private int currentExplosivesCount;
        private TickTimer explosiveRegenTimer;

        /// <summary>World positions of armed charges awaiting detonation (state authority only).</summary>
        private readonly List<Vector3> armedCharges = new List<Vector3>();

        public int ExplosivesRemaining => currentExplosivesCount;

        protected override void OnInitialize()
        {
            // [v0.11] className = "Demolitions";
            // [v0.11] description = "Explosives expert capable of breaching reinforced structures and creating area denial zones.";
            // (v0.12, M4) the Smith. The enum and file keep the old name; players see this one.
            className = "Smith";
            description = "Breacher. Makes doors where there are none, carries the heavy charges, and swings across gaps on a grapple line."; // (v0.24) the line is now the tactical

            // Demolitions has high health but slower movement
            maxHealth = 110f;
            movementSpeed = 4.5f;
            sprintSpeed = 6.5f;
            // [v0.23] movement.CanGrapple = true; movement.GrappleRange = 28f;          // v0.7: hook to reach breach points
            // (v0.24) The grapple is the Smith's tactical now (the Grapple Line), on the tactical key like
            //         everyone else's, and can be swapped for another. It is no longer built into the class.
            movement.GrappleRange = 28f;
            currentExplosivesCount = maxExplosives;

            // (v0.24) Two abilities. Placing and detonating were two keys for one idea: now the first press sets
            //         a charge and the next sets it off. The incendiary is gone.
            // [v0.23] DefineAbility(0, "Place Explosive",  cooldown: 2f,  staminaCost: 5f);
            // [v0.23] DefineAbility(1, "Detonate",         cooldown: 4f,  staminaCost: 0f);
            // [v0.23] DefineAbility(2, "Breaching Charge", cooldown: 20f, staminaCost: 20f);
            // [v0.23] DefineAbility(3, "Incendiary",       cooldown: 18f, staminaCost: 15f, duration: incendiaryDuration);
            DefineAbility(0, "Remote Charge",    cooldown: 1.2f, staminaCost: 5f);
            DefineAbility(1, "Breaching Charge", cooldown: 20f,  staminaCost: 20f);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                // [v0.23] case 0: return PlaceExplosive();
                // [v0.23] case 1: return DetonateExplosives();
                // [v0.23] case 2: return BreachingCharge();
                // [v0.23] case 3: return IncendiaryGrenade();
                // (v0.24) one charge at a time: down, then off
                case 0: return armedCharges.Count > 0 ? DetonateExplosives() : PlaceExplosive();
                case 1: return BreachingCharge();
            }
            return false;
        }

        /// <summary>(v0.13) What each ability shows in the world.</summary>
        public override Core.AbilityVisual GetAbilityVisual(int slot)
        {
            var fire = new Color(1f, 0.55f, 0.12f);
            switch (slot)
            {
                // [v0.23] case 0: return new Core.AbilityVisual(Core.AbilityVisualKind.PlaceCharge, explosiveRadius, 0f, fire);
                // [v0.23] case 1: return new Core.AbilityVisual(Core.AbilityVisualKind.DetonateCharges, explosiveRadius, 0f, fire);
                // [v0.23] case 2: return new Core.AbilityVisual(Core.AbilityVisualKind.Blast, 2.5f, 0f, fire, 1.5f);
                case 0: return new Core.AbilityVisual(Core.AbilityVisualKind.ToggleCharge, explosiveRadius, 0f, fire);
                case 1: return new Core.AbilityVisual(Core.AbilityVisualKind.Blast, 2.5f, 0f, fire, 1.5f);
                case 3: return new Core.AbilityVisual(Core.AbilityVisualKind.Fire, incendiaryRadius, 1.6f, fire, 3f);
                default: return default;
            }
        }

        private bool PlaceExplosive()
        {
            if (currentExplosivesCount <= 0)
            {
                Log("No explosives remaining");
                return false;
            }

            Log($"Placing explosive ({currentExplosivesCount - 1} remaining)");
            currentExplosivesCount--;

            if (context != null)
            {
                armedCharges.Add(context.Transform.position);
            }

            // Start regen when the first charge is spent
            if (context != null && !explosiveRegenTimer.IsRunning)
            {
                explosiveRegenTimer = StartTimer(explosiveRegenSeconds);
            }
            return true;
        }

        private bool DetonateExplosives()
        {
            if (armedCharges.Count == 0)
            {
                Log("Nothing to detonate");
                return false;
            }

            Log($"Detonating {armedCharges.Count} explosives - Radius: {explosiveRadius}m, Damage: {explosiveDamage}");
            if (context != null)
            {
                var self = Network.NetworkPlayer.Resolve(context);
                foreach (var pos in armedCharges)
                {
                    using (Combat.DamageContext.Begin(means: "Placed Charge")) // (v0.14)
                    {
                        Combat.DamageUtil.ApplyRadialDamage(pos, explosiveRadius, explosiveDamage, self, context.Team, falloff: true);
                    }
                }
            }
            armedCharges.Clear();
            return true;
        }

        private bool BreachingCharge()
        {
            Log("Deploying breaching charge on door/wall");
            if (context == null) return true;

            var breachable = Core.SceneUtil.FindNearest<Interaction.IBreachable>(context.Transform.position, breachRange);
            if (breachable == null)
            {
                Log("No breachable surface in range");
                return false;
            }
            breachable.Breach(context.Team);
            return true;
        }

        private bool IncendiaryGrenade()
        {
            Log("Throwing incendiary grenade");
            if (context == null) return true;

            Vector3 origin = context.EyePosition + context.AimDirection * 3f;
            var self = Network.NetworkPlayer.Resolve(context);
            foreach (var victim in Network.NetworkPlayer.FindPlayersInRadius(origin, incendiaryRadius, Core.TeamID.None, aliveOnly: true))
            {
                victim.ApplyBurning(incendiaryDuration, incendiaryDamagePerSecond, self);
            }
            return true;
        }

        protected override void OnUpdate(float deltaTime)
        {
            // [v0.1] // Regenerate explosives slowly over time (was a TODO)
            if (currentExplosivesCount < maxExplosives && explosiveRegenTimer.IsRunning && TimerExpired(explosiveRegenTimer))
            {
                currentExplosivesCount++;
                explosiveRegenTimer = currentExplosivesCount < maxExplosives ? StartTimer(explosiveRegenSeconds) : TickTimer.None;
            }
        }

        protected override void OnDeath()
        {
            armedCharges.Clear();
        }
    }
}
