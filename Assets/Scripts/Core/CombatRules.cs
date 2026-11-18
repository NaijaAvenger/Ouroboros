using System;

namespace Ouroboros.Core
{
    /// <summary>
    /// Pure combat arithmetic (v0.9, milestone M1). No Unity or Fusion types, so it is unit-tested outside the
    /// engine (Tools/tests). Weapons, guards and the HUD call into this; tune behaviour here and in
    /// <c>EquipmentData</c>, not in the callers.
    /// </summary>
    public static class CombatRules
    {
        // ------------------------------------------------------------------
        // Damage

        /// <summary>
        /// Damage multiplier for distance: 1 up to <paramref name="start"/>, falling linearly to
        /// <paramref name="minFraction"/> at <paramref name="end"/> and beyond. <c>end &lt;= start</c> disables falloff.
        /// </summary>
        public static float DamageFalloff(float distance, float start, float end, float minFraction)
        {
            if (end <= start) return 1f;
            float min = Clamp(minFraction, 0f, 1f);
            if (distance <= start) return 1f;
            if (distance >= end) return min;
            float t = (distance - start) / (end - start);
            return 1f + (min - 1f) * t;
        }

        /// <summary>Final damage of one pellet / bullet.</summary>
        public static float ShotDamage(float baseDamage, float falloffMultiplier, bool headshot, float headshotMultiplier)
        {
            float damage = baseDamage * falloffMultiplier;
            if (headshot) damage *= Math.Max(1f, headshotMultiplier);
            return Math.Max(0f, damage);
        }

        /// <summary>
        /// Fallback hit-zone test when the target has no hitboxes: the top <paramref name="headFraction"/> of the
        /// body counts as the head.
        /// </summary>
        public static bool IsHeadHeight(float hitHeightAboveFeet, float bodyHeight, float headFraction = 0.2f)
        {
            if (bodyHeight <= 0f) return false;
            return hitHeightAboveFeet >= bodyHeight * (1f - Clamp(headFraction, 0f, 1f));
        }

        // ------------------------------------------------------------------
        // Spread and bloom

        /// <summary>Bloom right after a shot: grows by <paramref name="perShot"/>, capped at <paramref name="max"/>.</summary>
        public static float BloomAfterShot(float currentBloom, float perShot, float max)
        {
            return Clamp(currentBloom + Math.Max(0f, perShot), 0f, Math.Max(0f, max));
        }

        /// <summary>Bloom left <paramref name="secondsSinceShot"/> after it was last raised (linear recovery).</summary>
        public static float DecayedBloom(float bloomAtShot, float secondsSinceShot, float recoveryPerSecond)
        {
            if (secondsSinceShot <= 0f) return Math.Max(0f, bloomAtShot);
            return Math.Max(0f, bloomAtShot - Math.Max(0f, recoveryPerSecond) * secondsSinceShot);
        }

        /// <summary>Cone half-angle in degrees for the next shot.</summary>
        public static float EffectiveSpread(float baseSpread, float bloom, float stanceMultiplier)
        {
            return Math.Max(0f, (Math.Max(0f, baseSpread) + Math.Max(0f, bloom)) * Math.Max(0f, stanceMultiplier));
        }

        /// <summary>
        /// Spread multiplier from what the shooter is doing. Standing still on the ground is 1.
        /// </summary>
        public static float StanceSpreadMultiplier(bool grounded, bool sprinting, bool sliding)
        {
            float m = 1f;
            if (!grounded) m *= 1.6f;
            if (sprinting) m *= 1.4f;
            if (sliding) m *= 1.25f;
            return m;
        }

        /// <summary>
        /// Seed for one pellet. Built only from values the shooter's client and the server agree on, so the
        /// predicted tracer and the authoritative hit use the same direction.
        /// </summary>
        public static int ShotSeed(int tick, int playerId, int slot, int pellet)
        {
            unchecked
            {
                int h = tick;
                h = h * 486187739 + playerId;
                h = h * 486187739 + slot;
                h = h * 486187739 + pellet;
                return h;
            }
        }

        /// <summary>
        /// Deterministic offset inside a cone of <paramref name="maxDegrees"/>, uniform over the disc.
        /// </summary>
        public static void SpreadOffset(int seed, float maxDegrees, out float yawDegrees, out float pitchDegrees)
        {
            if (maxDegrees <= 0f)
            {
                yawDegrees = 0f;
                pitchDegrees = 0f;
                return;
            }
            double radius = Math.Sqrt(Hash01(seed)) * maxDegrees;
            double angle = Hash01(unchecked(seed * 31 + 0x5bd1e995)) * Math.PI * 2.0;
            yawDegrees = (float)(Math.Cos(angle) * radius);
            pitchDegrees = (float)(Math.Sin(angle) * radius);
        }

        // ------------------------------------------------------------------
        // Recoil (view punch on the shooter's own camera)

        /// <summary>
        /// Kick for one shot. Pitch is always upward and grows a little over the first shots of a burst; yaw is
        /// a pseudo-random sway inside <paramref name="yawRange"/>.
        /// </summary>
        public static void RecoilKick(int shotIndexInBurst, float pitchPerShot, float yawRange, int seed, out float pitchUp, out float yaw)
        {
            int ramp = Math.Max(0, Math.Min(shotIndexInBurst, 5));
            pitchUp = Math.Max(0f, pitchPerShot) * (1f + 0.12f * ramp);
            yaw = (Hash01(seed) * 2f - 1f) * Math.Max(0f, yawRange);
        }

        /// <summary>Exponential return of the view punch toward zero.</summary>
        public static float RecoverRecoil(float offset, float recoveryPerSecond, float deltaTime)
        {
            if (recoveryPerSecond <= 0f || deltaTime <= 0f) return offset;
            return offset * (float)Math.Exp(-recoveryPerSecond * deltaTime);
        }

        // ------------------------------------------------------------------
        // Guards

        /// <summary>
        /// Chance for an AI shot to hit. Starts at <paramref name="baseAccuracy"/> point-blank against a
        /// stationary target, drops with distance (to 60% of base at the edge of <paramref name="attackRange"/>)
        /// and with how fast the target is moving; each alarm tier adds a little. Clamped to 5%..95% so a guard
        /// is never certain and never harmless.
        /// </summary>
        public static float GuardHitChance(float baseAccuracy, float distance, float attackRange, float targetSpeed, int alarmTier)
        {
            float range01 = attackRange > 0f ? Clamp(distance / attackRange, 0f, 1f) : 1f;
            float chance = baseAccuracy * (1f - 0.4f * range01);

            // 0 m/s = no penalty, 8 m/s (a sprint) = 45% harder to hit.
            float speed01 = Clamp(targetSpeed / 8f, 0f, 1f);
            chance *= 1f - 0.45f * speed01;

            chance += 0.05f * Math.Max(0, alarmTier);
            return Clamp(chance, 0.05f, 0.95f);
        }

        // ------------------------------------------------------------------
        // Helpers

        /// <summary>Deterministic value in [0, 1) from an integer seed.</summary>
        public static float Hash01(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x >> 16; x *= 0x7feb352d;
                x ^= x >> 15; x *= 0x846ca68b;
                x ^= x >> 16;
                return (x >> 8) / 16777216f; // 24 bits: exactly representable in a float, never 1.0
            }
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
    }
}
