using UnityEngine;
using Fusion;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Instant-hit weapon (rifles, pistols, shotguns). Fires on the state authority using Fusion's
    /// lag-compensated raycast so hits line up with what the shooter saw. Targets are anything with an
    /// <see cref="Combat.IDamageable"/> in the hit object's parents (players via their CharacterController
    /// collider, AI via their collider). For full lag compensation add <c>HitboxRoot</c>/<c>Hitbox</c>
    /// components to the targets; PhysX colliders are included either way.
    /// </summary>
    public class HitscanWeapon : BaseEquipment
    {
        private NetworkRunner hitboxRunner;
        private HitboxManager hitboxManager;

        /// <summary>True when the current runner carries a HitboxManager, without touching Runner.LagCompensation.</summary>
        private bool HasHitboxManager()
        {
            var runner = Runner;
            if (runner != hitboxRunner)
            {
                hitboxRunner = runner;
                hitboxManager = runner != null ? runner.GetComponent<HitboxManager>() : null;
            }
            return hitboxManager != null;
        }

        /// <summary>Start the ray this far ahead of the eyes so the shooter's own capsule is skipped.</summary>
        private const float MuzzleOffset = 0.6f;

        protected override bool OnUse()
        {
            if (data == null || Runner == null) return false;
            if (!loadout.ConsumeAmmo(slotIndex)) return false;

            Vector3 aim = owner.AimDirection;
            Vector3 origin = owner.EyePosition + aim * MuzzleOffset;
            float range = Mathf.Max(1f, data.range);
            int mask = data.hitMask.value == ~0 ? Physics.DefaultRaycastLayers : data.hitMask.value;
            float damagePerPellet = data.damage;

            bool hitAnything = false;
            Vector3 lastPoint = origin + aim * range;
            int pellets = Mathf.Max(1, data.pelletCount);

            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = ApplySpread(aim, data.spreadDegrees);

                GameObject hitObject = null;
                Vector3 hitPoint = origin + dir * range;
                bool didHit;

                // Lag-compensated when the runner has a HitboxManager (Lag Compensation enabled in NetworkProjectConfig);
                // plain physics otherwise.
                // [v0.4] previously called Runner.LagCompensation unconditionally → NullReferenceException on runners without it.
                // [v0.8] var lagComp = Runner.LagCompensation;  ← the getter itself logs "[Fusion] Failed to find HitboxManager"
                //        on every shot when lag compensation is off, so probe for the component first (cached per runner).
                var lagComp = HasHitboxManager() ? Runner.LagCompensation : null;
                if (lagComp != null)
                {
                    didHit = lagComp.Raycast(origin, dir, range, owner.Object.InputAuthority, out LagCompensatedHit hit, mask,
                        HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority);
                    if (didHit) { hitPoint = hit.Point; hitObject = hit.GameObject; }
                }
                else
                {
                    didHit = Physics.Raycast(origin, dir, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore);
                    if (didHit) { hitPoint = hit.point; hitObject = hit.collider.gameObject; }
                }

                lastPoint = hitPoint;
                if (didHit)
                {
                    hitAnything = true;
                    var target = hitObject != null ? hitObject.GetComponentInParent<Combat.IDamageable>() : null;
                    if (target != null && !ReferenceEquals(target, owner) && target.IsAlive)
                    {
                        target.ApplyDamage(damagePerPellet, owner);
                    }
                }
            }

            loadout.NotifyFired(slotIndex, lastPoint, hitAnything);
            return true;
        }

        private static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0f) return direction;
            Vector2 offset = Random.insideUnitCircle * degrees;
            return Quaternion.AngleAxis(offset.x, Vector3.up) * Quaternion.AngleAxis(offset.y, Vector3.right) * direction;
        }
    }
}
