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
            className = "Demolitions";
            description = "Explosives expert capable of breaching reinforced structures and creating area denial zones.";

            // Demolitions has high health but slower movement
            maxHealth = 110f;
            movementSpeed = 4.5f;
            sprintSpeed = 6.5f;
            movement.CanGrapple = true; movement.GrappleRange = 28f;          // v0.7: hook to reach breach points
            currentExplosivesCount = maxExplosives;

            DefineAbility(0, "Place Explosive",  cooldown: 2f,  staminaCost: 5f);
            DefineAbility(1, "Detonate",         cooldown: 4f,  staminaCost: 0f);
            DefineAbility(2, "Breaching Charge", cooldown: 20f, staminaCost: 20f);
            DefineAbility(3, "Incendiary",       cooldown: 18f, staminaCost: 15f, duration: incendiaryDuration);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return PlaceExplosive();
                case 1: return DetonateExplosives();
                case 2: return BreachingCharge();
                case 3: return IncendiaryGrenade();
            }
            return false;
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
                    Combat.DamageUtil.ApplyRadialDamage(pos, explosiveRadius, explosiveDamage, self, context.Team, falloff: true);
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
