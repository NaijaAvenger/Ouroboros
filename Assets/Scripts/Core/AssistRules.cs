using System;

namespace Ouroboros.Core
{
    /// <summary>(v0.17) What a crew ping marks. Decided on the state authority from what the pinger is looking at.</summary>
    public enum PingKind : byte
    {
        /// <summary>A place: "go here", "look here".</summary>
        Go = 0,
        /// <summary>Something that can hurt or report the crew: a rival, a guard, a turret, a camera.</summary>
        Enemy = 1,
        /// <summary>Something worth taking or opening: loot, a container, a vault, a case.</summary>
        Loot = 2,
        /// <summary>Something to use: a door, a terminal, a cash-out station, a lift.</summary>
        Device = 3,
        /// <summary>A downed crewmate's ID card.</summary>
        Revive = 4
    }

    /// <summary>
    /// (v0.17) Rules for the things crewmates do for each other: pings, revives, and health that comes back
    /// out of combat. No Unity or Fusion types, so the unit tests can run them.
    /// </summary>
    public static class AssistRules
    {
        // ---- pings

        /// <summary>A player may ping again once <paramref name="cooldownTicks"/> have passed since their last one.</summary>
        public static bool CanPing(int nowTick, int lastPingTick, int cooldownTicks)
        {
            return nowTick - lastPingTick >= Math.Max(0, cooldownTicks);
        }

        /// <summary>
        /// Seconds a ping stays up. A marked enemy goes stale fast because they move; a place or a thing does not.
        /// </summary>
        public static float PingLifetime(PingKind kind)
        {
            switch (kind)
            {
                case PingKind.Enemy:  return 5f;
                case PingKind.Revive: return 12f;
                case PingKind.Loot:   return 10f;
                default:              return 8f;
            }
        }

        /// <summary>How far a point is from a ray, and how far along the ray its nearest point lies. For pinging small things.</summary>
        public static float DistanceFromRay(float ox, float oy, float oz, float dx, float dy, float dz, float px, float py, float pz, out float along)
        {
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-6f) { along = 0f; return (float)Math.Sqrt((px - ox) * (px - ox) + (py - oy) * (py - oy) + (pz - oz) * (pz - oz)); }
            dx /= length; dy /= length; dz /= length;
            along = (px - ox) * dx + (py - oy) * dy + (pz - oz) * dz;
            float t = Math.Max(0f, along);
            float cx = ox + dx * t, cy = oy + dy * t, cz = oz + dz * t;
            return (float)Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy) + (pz - cz) * (pz - cz));
        }

        // ---- revive

        /// <summary>
        /// One step of a revive, as a fraction from 0 to 1. It fills while a crewmate works the card and drains
        /// when they let go, so a revive can be interrupted but not banked for later.
        /// </summary>
        public static float ReviveStep(float progress, bool crewmateWorking, float deltaTime, float reviveSeconds, float drainPerSecond)
        {
            float p = Clamp01(progress);
            if (deltaTime <= 0f) return p;
            if (crewmateWorking) return reviveSeconds <= 0f ? 1f : Clamp01(p + deltaTime / reviveSeconds);
            return Clamp01(p - Math.Max(0f, drainPerSecond) * deltaTime);
        }

        /// <summary>Health a revived player stands up with: a fraction of full, and never less than 1.</summary>
        public static float ReviveHealth(float maxHealth, float fraction)
        {
            if (maxHealth <= 0f) return 0f;
            return Math.Max(1f, Math.Min(maxHealth, maxHealth * Clamp01(fraction)));
        }

        // ---- health that comes back

        /// <summary>
        /// Health after one step of regeneration. Nothing happens until <paramref name="delaySeconds"/> have
        /// passed without taking damage; then health climbs at <paramref name="perSecond"/> up to
        /// <paramref name="capFraction"/> of full. Health above the cap (from a medic) is left alone.
        /// A rate of zero or less switches regeneration off.
        /// </summary>
        public static float RegenStep(float health, float maxHealth, float secondsSinceDamage, float delaySeconds, float perSecond, float capFraction, float deltaTime)
        {
            if (health <= 0f || maxHealth <= 0f || perSecond <= 0f || deltaTime <= 0f) return health;
            if (secondsSinceDamage < Math.Max(0f, delaySeconds)) return health;
            float cap = maxHealth * Clamp01(capFraction);
            if (health >= cap) return health;
            return Math.Min(cap, health + perSecond * deltaTime);
        }

        /// <summary>True while regeneration is adding health: for the HUD.</summary>
        public static bool IsRegenerating(float health, float maxHealth, float secondsSinceDamage, float delaySeconds, float perSecond, float capFraction)
        {
            return health > 0f && perSecond > 0f && secondsSinceDamage >= Math.Max(0f, delaySeconds) && health < maxHealth * Clamp01(capFraction);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
