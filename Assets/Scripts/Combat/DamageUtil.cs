using UnityEngine;

namespace Ouroboros.Combat
{
    /// <summary>
    /// Shared damage helpers. All methods are state-authority only; they iterate the static
    /// registries kept by <c>NetworkPlayer</c> and <c>BaseAIAgent</c> instead of running physics
    /// overlaps, which keeps them deterministic and allocation-light.
    /// </summary>
    public static class DamageUtil
    {
        /// <summary>
        /// Applies damage to every damageable within <paramref name="radius"/> of <paramref name="origin"/>.
        /// Friendly-fire filtering happens inside <see cref="IDamageable.ApplyDamage"/>.
        /// </summary>
        /// <param name="falloff">If true, damage scales linearly from full at the centre to 25% at the edge.</param>
        /// <param name="requireLineOfSight">If true, a raycast against <paramref name="occluders"/> must reach the target.</param>
        public static int ApplyRadialDamage(Vector3 origin, float radius, float damage, Network.NetworkPlayer attacker,
            Core.TeamID attackerTeam, bool falloff = true, bool requireLineOfSight = false, LayerMask occluders = default)
        {
            int hit = 0;
            float radiusSq = radius * radius;

            // Players
            for (int i = 0; i < Network.NetworkPlayer.All.Count; i++)
            {
                var target = Network.NetworkPlayer.All[i];
                if (target == null || !target.IsAlive) continue;
                if (TryHit(target, target.Position, origin, radius, radiusSq, damage, attacker, falloff, requireLineOfSight, occluders))
                {
                    hit++;
                }
            }

            // AI
            for (int i = 0; i < AI.BaseAIAgent.All.Count; i++)
            {
                var target = AI.BaseAIAgent.All[i];
                if (target == null || !target.IsAlive) continue;
                if (TryHit(target, target.Position, origin, radius, radiusSq, damage, attacker, falloff, requireLineOfSight, occluders))
                {
                    hit++;
                }
            }

            return hit;
        }

        private static bool TryHit(IDamageable target, Vector3 targetPos, Vector3 origin, float radius, float radiusSq,
            float damage, Network.NetworkPlayer attacker, bool falloff, bool requireLineOfSight, LayerMask occluders)
        {
            Vector3 delta = targetPos - origin;
            float distSq = delta.sqrMagnitude;
            if (distSq > radiusSq) return false;

            if (requireLineOfSight)
            {
                float dist = Mathf.Sqrt(distSq);
                if (dist > 0.01f && Physics.Raycast(origin, delta / dist, dist, occluders))
                {
                    return false;
                }
            }

            float scaled = damage;
            if (falloff && radius > 0f)
            {
                float t = Mathf.Clamp01(Mathf.Sqrt(distSq) / radius);
                scaled = Mathf.Lerp(damage, damage * 0.25f, t);
            }

            target.ApplyDamage(scaled, attacker);
            return true;
        }

        /// <summary>
        /// Hitscan from <paramref name="origin"/> along <paramref name="direction"/>. Returns the damageable hit, if any.
        /// </summary>
        public static IDamageable Hitscan(Vector3 origin, Vector3 direction, float range, float damage,
            Network.NetworkPlayer attacker, LayerMask hitMask)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) return null;

            damageable.ApplyDamage(damage, attacker);
            return damageable;
        }
    }
}
